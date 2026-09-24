import os
import sys
import httpx
import anyio
import asyncio
import uvicorn
from mcp.server.mcpserver import MCPServer
from mcp.types import Tool
from pydantic import create_model, Field
import typing

mcp = MCPServer("Civil 3D MCP Server")

ARBA_HOST = "127.0.0.1"
ARBA_PORT = int(os.environ.get("ARBA_MCP_PORT", 8765))
BASE_URL = f"http://{ARBA_HOST}:{ARBA_PORT}"

_cached_token = None
def _get_token() -> str:
    global _cached_token
    if _cached_token:
        return _cached_token
    token_path = os.path.expandvars(r"%LOCALAPPDATA%\ArbaMcp\token")
    try:
        with open(token_path, "r", encoding="utf-8") as f:
            _cached_token = f.read().strip()
    except Exception as e:
        print(f"Warning: could not read token: {e}")
        _cached_token = ""
    return _cached_token

def _clear_token():
    global _cached_token
    _cached_token = None

_http_client = None
def _get_client():
    global _http_client
    if _http_client is None or _http_client.is_closed:
        _http_client = httpx.AsyncClient(base_url=BASE_URL, limits=httpx.Limits(max_keepalive_connections=10, max_connections=20))
    return _http_client

async def _c3d_execute(tool_name: str, args: dict, retry=True):
    try:
        client = _get_client()
        headers = {"Content-Type": "application/json"}
        token = _get_token()
        if token:
            headers["X-Arba-Token"] = token
            
        response = await client.post("/execute", json={"tool": tool_name, "args": args, "timeout_s": 120}, headers=headers, timeout=120.0)
        
        if response.status_code == 401 and retry:
            _clear_token()
            return await _c3d_execute(tool_name, args, retry=False)
            
        if response.status_code != 200:
            return f"Error HTTP {response.status_code} de Civil 3D"
            
        data = response.json()
        if not data.get("ok"):
            return f"Error de Civil 3D: {data.get('error')}"
        return data.get("result", data)
    except httpx.ConnectError:
        return "Civil 3D no está abierto o ArbaMcp no cargó; pulsa Conexión IA en la pestaña ARBA"
    except Exception as e:
        return f"Error de conexión: {e}"

def type_to_python(t: str):
    if t == "number": return float
    if t == "boolean": return bool
    return str

known_tools = set()

async def update_tools_loop():
    global known_tools
    while True:
        try:
            client = _get_client()
            headers = {}
            token = _get_token()
            if token:
                headers["X-Arba-Token"] = token
                
            res = await client.get("/tools", headers=headers, timeout=5.0)
            if res.status_code == 401:
                _clear_token()
            elif res.status_code == 200:
                data = res.json()
                if data.get("ok"):
                    current_tool_names = set(t["name"] for t in data.get("tools", []))
                    for tname in list(known_tools):
                        if tname not in current_tool_names:
                            mcp.remove_tool(tname)
                            known_tools.remove(tname)
                    for tool_info in data.get("tools", []):
                        if tool_info["name"] not in known_tools:
                            fields = {}
                            for p in tool_info.get("parameters", []):
                                ptype = type_to_python(p["type"])
                                desc = p.get("description", "")
                                if p.get("required", False):
                                    fields[p["name"]] = (ptype, Field(..., description=desc))
                                else:
                                    fields[p["name"]] = (typing.Optional[ptype], Field(None, description=desc))
                            
                            SchemaModel = create_model(f"{tool_info['name']}_Model", **fields)
                            
                            def make_handler(tname):
                                async def handler(args: SchemaModel) -> str:
                                    return str(await _c3d_execute(tname, args.model_dump(exclude_none=True)))
                                return handler
                            
                            handler = make_handler(tool_info["name"])
                            handler.__name__ = tool_info["name"]
                            handler.__doc__ = tool_info.get("description", "")
                            
                            mcp.add_tool(handler)
                            known_tools.add(tool_info["name"])
        except Exception as e:
            pass
        await asyncio.sleep(5)

async def run_combined_async():
    asyncio.create_task(update_tools_loop())
    http_app = mcp.streamable_http_app(host="127.0.0.1", stateless_http=True, json_response=True)
    sse_app = mcp.sse_app(host="127.0.0.1")
    for route in sse_app.routes:
        http_app.routes.append(route)
    config = uvicorn.Config(http_app, host="127.0.0.1", port=8001, log_level="info")
    server = uvicorn.Server(config)
    await server.serve()

if __name__ == "__main__":
    anyio.run(run_combined_async)

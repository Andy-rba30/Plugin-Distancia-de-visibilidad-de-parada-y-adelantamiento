import os
import sys
import httpx
import anyio
import uvicorn
from mcp.server.mcpserver import MCPServer
from pydantic import create_model, Field
import typing

mcp = MCPServer("Civil 3D MCP Server")

ARBA_HOST = "127.0.0.1"
ARBA_PORT = int(os.environ.get("ARBA_MCP_PORT", 8765))
BASE_URL = f"http://{ARBA_HOST}:{ARBA_PORT}"

_http_client = None
def _get_client():
    global _http_client
    if _http_client is None or _http_client.is_closed:
        _http_client = httpx.AsyncClient(base_url=BASE_URL, limits=httpx.Limits(max_keepalive_connections=10, max_connections=20))
    return _http_client

async def _c3d_execute(tool_name: str, args: dict):
    try:
        client = _get_client()
        response = await client.post("/execute", json={"tool": tool_name, "args": args, "timeout_s": 120}, headers={"Content-Type": "application/json"}, timeout=120.0)
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

def register_tools():
    try:
        with httpx.Client(base_url=BASE_URL) as client:
            res = client.get("/tools", timeout=5.0)
            if res.status_code == 200:
                data = res.json()
                if data.get("ok"):
                    for tool_info in data.get("tools", []):
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
                        
                        mcp.tool()(handler)
    except Exception as e:
        print(f"Warning: Could not fetch tools from Civil 3D at startup: {e}")

register_tools()

async def run_combined_async():
    http_app = mcp.streamable_http_app(host="127.0.0.1", stateless_http=True, json_response=True)
    sse_app = mcp.sse_app(host="127.0.0.1")
    for route in sse_app.routes:
        http_app.routes.append(route)
    config = uvicorn.Config(http_app, host="127.0.0.1", port=8001, log_level="info")
    server = uvicorn.Server(config)
    await server.serve()

if __name__ == "__main__":
    anyio.run(run_combined_async)

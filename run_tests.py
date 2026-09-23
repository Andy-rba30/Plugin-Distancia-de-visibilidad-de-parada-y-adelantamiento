import httpx
import os
import sys

BASE_URL = "http://127.0.0.1:8765"

token_path = os.path.expandvars(r"%LOCALAPPDATA%\ArbaMcp\token")
try:
    with open(token_path, "r", encoding="utf-8") as f:
        TOKEN = f.read().strip()
except Exception as e:
    print(f"Error reading token: {e}")
    sys.exit(1)

print(f"--- Token actual: {TOKEN} ---")

def test_req(name, method, endpoint, headers, json=None, timeout=120):
    try:
        print(f"\n[TEST] {name}")
        res = httpx.request(method, f"{BASE_URL}{endpoint}", headers=headers, json=json, timeout=timeout)
        print(f"Status: {res.status_code}")
        print(f"Body: {res.text}")
    except Exception as e:
        print(f"Exception: {e}")

# 1. Sin token
test_req("Sin token (401)", "GET", "/ping", {})

# 2. Con token
test_req("Con token (200)", "GET", "/ping", {"X-Arba-Token": TOKEN})

# 3. Con Origin
test_req("Con Origin (403)", "GET", "/ping", {"X-Arba-Token": TOKEN, "Origin": "http://localhost:3000"})

# 4. POST sin Content-Type
# httpx automatically sets Content-Type to application/json if json=... is used.
# So we pass data instead or remove the header.
try:
    print("\n[TEST] POST sin Content-Type (415)")
    res = httpx.post(f"{BASE_URL}/execute", content="{}", headers={"X-Arba-Token": TOKEN, "Content-Type": "text/plain"}, timeout=5)
    print(f"Status: {res.status_code}")
    print(f"Body: {res.text}")
except Exception as e:
    print(f"Exception: {e}")

# 5. Comando REGEN
test_req("Comando REGEN (Terminado)", "POST", "/execute", {"X-Arba-Token": TOKEN, "Content-Type": "application/json"}, {"tool": "ejecutar_comando", "args": {"comando": "REGEN"}})

# 6. Comando inexistente
test_req("Comando INEXISTENTE (No iniciado)", "POST", "/execute", {"X-Arba-Token": TOKEN, "Content-Type": "application/json"}, {"tool": "ejecutar_comando", "args": {"comando": "COMANDOINEXISTENTE123"}})

# 7. Comando _.LINE
test_req("Comando _.LINE (Timeout con ESC)", "POST", "/execute", {"X-Arba-Token": TOKEN, "Content-Type": "application/json"}, {"tool": "ejecutar_comando", "args": {"comando": "_.LINE"}, "timeout_s": 5})


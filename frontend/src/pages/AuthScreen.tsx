import React from "react";
import { FlowarrMark } from "../components/FlowarrMark";
import { postJson } from "../api";
import { Input } from "../components/Input";

export function AuthScreen({ setupRequired, onAuthenticated }: { setupRequired: boolean; onAuthenticated: () => void }) {
  const [username, setUsername] = React.useState("admin");
  const [password, setPassword] = React.useState("");
  const [message, setMessage] = React.useState("");

  async function submit() {
    setMessage(setupRequired ? "Creating admin..." : "Logging in...");
    try {
      await postJson(setupRequired ? "/api/auth/setup" : "/api/auth/login", { username, password });
      onAuthenticated();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Authentication failed.");
    }
  }

  return (
    <div className="grid min-h-screen place-items-center bg-bg px-4">
      <div className="w-full max-w-sm rounded-lg border border-line bg-surface p-5">
        <div className="mb-5 flex items-center gap-3">
          <div className="grid h-10 w-10 place-items-center rounded-md bg-accent text-bg">
            <FlowarrMark size={28} />
          </div>
          <div>
            <h1 className="text-lg font-semibold text-ink">Flowarr</h1>
            <p className="text-sm text-muted">{setupRequired ? "First-run admin setup" : "Admin login"}</p>
          </div>
        </div>
        <div className="grid gap-3">
          <Input label="Username" value={username} onChange={setUsername} />
          <Input label="Password" type="password" value={password} onChange={setPassword} />
          {setupRequired && <p className="text-xs text-muted">Use at least 12 characters. The password is stored as a salted hash.</p>}
          <button onClick={submit} className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-bg">
            {setupRequired ? "Create admin" : "Login"}
          </button>
          {message && <p className="text-sm text-muted">{message}</p>}
        </div>
      </div>
    </div>
  );
}

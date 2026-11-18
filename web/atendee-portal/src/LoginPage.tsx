import { useState } from "react";
import type { FormEvent } from "react";

type LoginState = "credentials" | "challenge" | "setup" | "recovery" | "authenticated" | "pending-owner";

type JsonBody = {
  state?: string;
  error?: string;
  sharedKey?: string;
  authenticatorUri?: string;
  recoveryCodes?: string[];
};

async function readJson(response: Response): Promise<JsonBody> {
  try {
    const body: unknown = await response.json();
    return body && typeof body === "object" ? (body as JsonBody) : {};
  } catch {
    return {};
  }
}

async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/api/v1/auth/antiforgery", {
    method: "GET",
    credentials: "same-origin",
    cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: { Accept: "application/json" }
  });

  if (!response.ok) throw new Error("Não foi possível preparar a validação de segurança.");
  const body = await readJson(response);
  if (!body || typeof (body as { requestToken?: unknown }).requestToken !== "string") {
    throw new Error("A validação de segurança não foi disponibilizada.");
  }
  return (body as { requestToken: string }).requestToken;
}

async function postJson(path: string, body?: object): Promise<{ response: Response; payload: JsonBody }> {
  const csrfToken = await getAntiforgeryToken();
  const headers: Record<string, string> = {
    Accept: "application/json",
    "X-CSRF-TOKEN": csrfToken
  };
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const response = await fetch(path, {
    method: "POST",
    credentials: "same-origin",
    cache: "no-store",
    referrerPolicy: "no-referrer",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body)
  });

  return { response, payload: await readJson(response) };
}

export default function LoginPage() {
  const [stage, setStage] = useState<LoginState>("credentials");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [sharedKey, setSharedKey] = useState("");
  const [authenticatorUri, setAuthenticatorUri] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [membershipPending, setMembershipPending] = useState(false);
  const [recoveryAcknowledged, setRecoveryAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function beginEnrollment() {
    setStage("setup");
    setError(null);
    setNotice("Preparando a autenticação em duas etapas…");
    try {
      const { response, payload } = await postJson("/api/v1/auth/mfa/enrollment/start");
      if (!response.ok || !payload.sharedKey || !payload.authenticatorUri) {
        setStage("credentials");
        setError(response.status === 401
          ? "A etapa de configuração expirou. Entre novamente para continuar."
          : "Não foi possível iniciar a configuração da autenticação em duas etapas.");
        setNotice(null);
        return;
      }
      setSharedKey(payload.sharedKey);
      setAuthenticatorUri(payload.authenticatorUri);
      setNotice(null);
    } catch {
      setStage("credentials");
      setError("Não foi possível iniciar a configuração segura. Entre novamente.");
      setNotice(null);
    }
  }

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      const { response, payload } = await postJson("/api/v1/auth/login", { email, password });
      if (response.status === 202 && payload.state === "mfa_required") {
        setPassword("");
        setCode("");
        setUseRecoveryCode(false);
        setStage("challenge");
        setNotice("Informe o código do seu aplicativo autenticador.");
      } else if (response.status === 202 && payload.state === "mfa_setup_required") {
        setPassword("");
        await beginEnrollment();
      } else if (response.ok && payload.state === "authenticated") {
        setPassword("");
        setStage("authenticated");
        setNotice("Sua autenticação foi concluída.");
      } else {
        setError("E-mail ou senha inválidos ou conta indisponível.");
      }
    } catch {
      setError("Não foi possível concluir o acesso agora. Verifique sua conexão e tente novamente.");
    } finally {
      setBusy(false);
    }
  }

  async function handleChallenge(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      const { response } = await postJson("/api/v1/auth/mfa/challenge", {
        code,
        useRecoveryCode
      });
      if (response.ok) {
        setCode("");
        setStage("authenticated");
        setNotice("Autenticação em duas etapas concluída.");
      } else if (response.status === 429) {
        setError("Muitas tentativas. Aguarde um momento antes de tentar novamente.");
      } else {
        setError("Código inválido, expirado ou já utilizado. Confira e tente novamente.");
      }
    } catch {
      setError("Não foi possível validar o código agora. Tente novamente.");
    } finally {
      setBusy(false);
    }
  }

  async function handleEnrollment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      const { response, payload } = await postJson(
        "/api/v1/auth/mfa/enrollment/verify",
        { code }
      );
      if (!response.ok || !Array.isArray(payload.recoveryCodes) ||
          !payload.recoveryCodes.every(value => typeof value === "string")) {
        setError(response.status === 401
          ? "A etapa de configuração expirou ou foi bloqueada. Entre novamente."
          : "Código inválido. Confira o aplicativo autenticador e tente novamente.");
        return;
      }

      setRecoveryCodes(payload.recoveryCodes);
      setMembershipPending(payload.state === "mfa_enrolled_membership_pending");
      setRecoveryAcknowledged(false);
      setCode("");
      setStage("recovery");
    } catch {
      setError("Não foi possível concluir a configuração agora. Tente novamente.");
    } finally {
      setBusy(false);
    }
  }

  async function cancelEnrollment() {
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      const { response } = await postJson("/api/v1/auth/mfa/enrollment/cancel");
      if (response.ok) {
        setSharedKey("");
        setAuthenticatorUri("");
        setStage("credentials");
        setNotice("Configuração cancelada. Entre novamente para recomeçar.");
      } else {
        setError("Não foi possível cancelar a configuração. Atualize a página e tente novamente.");
      }
    } catch {
      setError("Não foi possível cancelar a configuração agora.");
    } finally {
      setBusy(false);
    }
  }

  async function copyRecoveryCodes() {
    try {
      await navigator.clipboard.writeText(recoveryCodes.join("\n"));
      setNotice("Códigos de recuperação copiados. Guarde-os em um local seguro.");
    } catch {
      setError("A cópia automática não está disponível. Selecione e copie os códigos manualmente.");
    }
  }

  function finishRecovery() {
    setRecoveryCodes([]);
    setSharedKey("");
    setAuthenticatorUri("");
    setStage(membershipPending ? "pending-owner" : "authenticated");
    setNotice(null);
  }

  return (
    <main className="page-shell">
      <section className="activation-card auth-card" aria-labelledby="page-title">
        <div className="brand-lockup" aria-label="Atendee Beauty">
          <span className="brand-mark" aria-hidden="true">a</span>
          <span className="brand-name">atendee<span>beauty</span></span>
        </div>

        <div className="eyebrow">ACESSO SEGURO</div>
        <h1 id="page-title">
          {stage === "credentials" && "Entre na sua conta"}
          {stage === "challenge" && "Confirme sua identidade"}
          {stage === "setup" && "Configure sua autenticação"}
          {stage === "recovery" && "Guarde seus códigos"}
          {stage === "authenticated" && "Acesso confirmado"}
          {stage === "pending-owner" && "Configuração concluída"}
        </h1>
        <p className="intro">
          {stage === "credentials" && "Acesse seu espaço de trabalho com seu e-mail e senha."}
          {stage === "challenge" && "Digite o código temporário do seu aplicativo autenticador ou use um código de recuperação."}
          {stage === "setup" && "Adicione sua conta ao aplicativo autenticador antes de confirmar o primeiro código."}
          {stage === "recovery" && "Estes códigos são exibidos uma única vez. Guarde-os antes de continuar."}
          {stage === "authenticated" && "Sua identidade foi autenticada. A sessão segura foi estabelecida pelo servidor."}
          {stage === "pending-owner" && "A autenticação em duas etapas está ativa. A associação inicial ao salão ainda precisa ser concluída pelo processo operacional protegido."}
        </p>

        {stage === "credentials" && (
          <form onSubmit={handleLogin} noValidate>
            <label htmlFor="email">E-mail</label>
            <input id="email" type="email" autoComplete="username" maxLength={256}
              value={email} onChange={event => setEmail(event.target.value)}
              required disabled={busy} />
            <label htmlFor="password">Senha</label>
            <input id="password" type="password" autoComplete="current-password" maxLength={1024}
              value={password} onChange={event => setPassword(event.target.value)}
              required disabled={busy} />
            <button type="submit" disabled={busy || !email.trim() || !password}>
              {busy ? "Verificando…" : "Entrar"}
            </button>
          </form>
        )}

        {stage === "challenge" && (
          <form onSubmit={handleChallenge} noValidate>
            <label htmlFor="mfa-code">{useRecoveryCode ? "Código de recuperação" : "Código de autenticação"}</label>
            <input id="mfa-code" type="text" autoComplete="one-time-code" inputMode="text"
              maxLength={64} value={code} onChange={event => setCode(event.target.value)}
              required disabled={busy} />
            <label className="check-row">
              <input type="checkbox" checked={useRecoveryCode}
                onChange={event => { setUseRecoveryCode(event.target.checked); setCode(""); }}
                disabled={busy} />
              Usar um código de recuperação
            </label>
            <button type="submit" disabled={busy || !code.trim()}>
              {busy ? "Validando…" : "Confirmar código"}
            </button>
          </form>
        )}

        {stage === "setup" && (
          <div className="setup-panel">
            {notice && <p className="feedback" role="status">{notice}</p>}
            {sharedKey ? (
              <>
                <p className="field-hint">No aplicativo autenticador, adicione uma conta manualmente e informe esta chave:</p>
                <div className="secret-value" aria-label="Chave de configuração">{sharedKey}</div>
                <p className="field-hint">Parâmetros: código de 6 dígitos, período de 30 segundos e SHA-1.</p>
                <details>
                  <summary>Mostrar URI de configuração</summary>
                  <code className="authenticator-uri">{authenticatorUri}</code>
                </details>
                <form onSubmit={handleEnrollment} noValidate>
                  <label htmlFor="enrollment-code">Código gerado pelo aplicativo</label>
                  <input id="enrollment-code" type="text" autoComplete="one-time-code"
                    inputMode="numeric" maxLength={16} value={code}
                    onChange={event => setCode(event.target.value)} required disabled={busy} />
                  <button type="submit" disabled={busy || !code.trim()}>
                    {busy ? "Confirmando…" : "Ativar autenticação"}
                  </button>
                </form>
                <button className="secondary-button" type="button" onClick={cancelEnrollment} disabled={busy}>
                  Cancelar configuração
                </button>
              </>
            ) : (
              <p className="field-hint">Aguarde enquanto a configuração segura é preparada.</p>
            )}
          </div>
        )}

        {stage === "recovery" && (
          <div className="recovery-panel">
            <p className="field-hint">Cada código pode ser usado uma única vez. Eles não poderão ser exibidos novamente.</p>
            <div className="recovery-codes">
              {recoveryCodes.map((recoveryCode, index) => <code key={index}>{recoveryCode}</code>)}
            </div>
            <button className="secondary-button" type="button" onClick={copyRecoveryCodes}>
              Copiar códigos
            </button>
            <label className="check-row">
              <input type="checkbox" checked={recoveryAcknowledged}
                onChange={event => setRecoveryAcknowledged(event.target.checked)} />
              Guardei meus códigos de recuperação em local seguro.
            </label>
            <button type="button" disabled={!recoveryAcknowledged} onClick={finishRecovery}>
              Concluir
            </button>
          </div>
        )}

        {(stage === "authenticated" || stage === "pending-owner") && (
          <div className="success-panel" role="status" aria-live="polite">
            <span className="status-icon" aria-hidden="true">✓</span>
            <div>
              <strong>{stage === "authenticated" ? "Autenticação concluída" : "MFA ativada"}</strong>
              <p>{stage === "authenticated"
                ? "Você concluiu a autenticação. A sessão é mantida pelo cookie seguro do servidor."
                : "Nenhuma sessão de salão foi criada porque ainda não existe vínculo com um salão. O primeiro Owner deve ser associado pelo comando operacional protegido."}</p>
            </div>
          </div>
        )}

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && stage !== "setup" && <p className="feedback success" role="status">{notice}</p>}

        <div className="security-note">
          <span aria-hidden="true">▣</span>
          <p>O acesso usa cookies seguros e validação antifalsificação. Nunca compartilhe sua senha, códigos temporários ou códigos de recuperação.</p>
        </div>
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}
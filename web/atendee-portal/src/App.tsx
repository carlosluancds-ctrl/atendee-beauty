import { useState } from "react";
import type { FormEvent } from "react";
import { validateNewPassword } from "./activation.mjs";

type Props = {
  activationToken: string | null;
};

type ApiError = {
  error?: string;
};

async function readJson(response: Response): Promise<Record<string, unknown>> {
  try {
    const body: unknown = await response.json();
    return body && typeof body === "object"
      ? (body as Record<string, unknown>)
      : {};
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

  if (!response.ok) {
    throw new Error("Não foi possível preparar a validação de segurança.");
  }

  const body = await readJson(response);
  if (typeof body.requestToken !== "string" || body.requestToken.length === 0) {
    throw new Error("A validação de segurança não foi disponibilizada.");
  }

  return body.requestToken;
}

export default function App({ activationToken }: Props) {
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [token, setToken] = useState(activationToken);
  const [error, setError] = useState<string | null>(
    activationToken ? null : "Este link está incompleto ou malformado. Abra o link mais recente enviado por e-mail."
  );
  const [complete, setComplete] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (!token) {
      setError("Este link de ativação não é válido. Solicite um novo link ao suporte.");
      return;
    }

    const passwordError = validateNewPassword(password);
    if (passwordError) {
      setError(passwordError);
      return;
    }

    if (password !== confirmation) {
      setError("As senhas não coincidem.");
      return;
    }

    setSubmitting(true);
    try {
      const csrfToken = await getAntiforgeryToken();
      const response = await fetch("/api/v1/auth/activate", {
        method: "POST",
        credentials: "same-origin",
        cache: "no-store",
        referrerPolicy: "no-referrer",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": csrfToken
        },
        body: JSON.stringify({ token, password })
      });

      const body = (await readJson(response)) as ApiError & { state?: string };

      if (response.ok && body.state === "activated_sign_in_required") {
        setComplete(true);
        setToken(null);
        setPassword("");
        setConfirmation("");
        return;
      }

      if (body.error === "password_policy_failed") {
        setError("A senha não atende à política de segurança. Use pelo menos 12 caracteres e 4 caracteres diferentes.");
      } else if (body.error === "antiforgery_validation_failed") {
        setError("A validação de segurança expirou. Atualize a página e tente novamente.");
      } else {
        setError("Este link expirou, já foi utilizado ou foi substituído por um link mais recente.");
      }
    } catch {
      setError("Não foi possível concluir a ativação agora. Verifique sua conexão e tente novamente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page-shell">
      <section className="activation-card" aria-labelledby="page-title">
        <div className="brand-lockup" aria-label="Atendee Beauty">
          <span className="brand-mark" aria-hidden="true">a</span>
          <span className="brand-name">atendee<span>beauty</span></span>
        </div>

        <div className="eyebrow">ACESSO SEGURO</div>
        <h1 id="page-title">{complete ? "Conta ativada" : "Crie sua senha"}</h1>
        <p className="intro">
          {complete
            ? "Sua conta foi ativada. O próximo passo é entrar com seu e-mail e a senha que acabou de criar."
            : "Defina uma senha pessoal para concluir a ativação da sua conta Atendee Beauty."}
        </p>

        {complete ? (
          <div className="success-panel" role="status" aria-live="polite">
            <span className="status-icon" aria-hidden="true">✓</span>
            <div>
              <strong>Ativação concluída</strong>
              <p>Sua senha foi definida. Por segurança, esta etapa não inicia uma sessão automaticamente.</p>
              <p>Você precisará entrar e concluir a configuração da autenticação em duas etapas.</p>
            </div>
          </div>
        ) : (
          <form onSubmit={handleSubmit} noValidate>
            <label htmlFor="password">Nova senha</label>
            <input
              id="password"
              name="password"
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              minLength={12}
              maxLength={1024}
              required
              disabled={!token || submitting}
              aria-describedby="password-hint"
            />
            <p className="field-hint" id="password-hint">Mínimo de 12 caracteres e pelo menos 4 caracteres diferentes.</p>

            <label htmlFor="confirmation">Confirme sua senha</label>
            <input
              id="confirmation"
              name="confirmation"
              type="password"
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              maxLength={1024}
              required
              disabled={!token || submitting}
            />

            {error && <p className="feedback error" role="alert">{error}</p>}

            <button type="submit" disabled={!token || submitting}>
              {submitting ? "Ativando conta…" : "Ativar minha conta"}
            </button>
          </form>
        )}

        <div className="security-note">
          <span aria-hidden="true">▣</span>
          <p>Este link é individual, de uso único e expira em 30 minutos. Nunca compartilhe sua senha ou este link.</p>
        </div>
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}
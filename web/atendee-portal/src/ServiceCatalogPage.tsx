import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";

type TenantProfile = {
  tenantId: string;
  name: string;
  slug: string;
  timeZoneId: string;
  status: string;
  membership: { displayName: string; role: "Owner" | "Manager" | "Receptionist" | "Professional" };
};
type SalonService = {
  id: string; name: string; durationMinutes: number; priceMinorUnits: number; currency: string;
  bufferBeforeMinutes: number; bufferAfterMinutes: number; isActive: boolean;
  createdAtUtc: string; updatedAtUtc: string;
};
type FormState = {
  name: string; durationMinutes: string; priceMinorUnits: string; currency: string;
  bufferBeforeMinutes: string; bufferAfterMinutes: string; isActive: "" | "true" | "false";
};
const emptyForm: FormState = {
  name: "", durationMinutes: "", priceMinorUnits: "", currency: "",
  bufferBeforeMinutes: "", bufferAfterMinutes: "", isActive: ""
};

async function readBody(response: Response): Promise<unknown> {
  try { return await response.json(); } catch { return null; }
}
async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/api/v1/auth/antiforgery", {
    method: "GET", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
  });
  if (!response.ok) throw new Error("Não foi possível preparar a validação de segurança.");
  const body = await readBody(response) as { requestToken?: unknown } | null;
  if (!body || typeof body.requestToken !== "string" || !body.requestToken)
    throw new Error("A validação de segurança não foi disponibilizada.");
  return body.requestToken;
}
async function sendMutation(path: string, method: "POST" | "PUT", body: object): Promise<Response> {
  const token = await getAntiforgeryToken();
  return fetch(path, {
    method, credentials: "same-origin", cache: "no-store", referrerPolicy: "no-referrer",
    headers: { Accept: "application/json", "Content-Type": "application/json", "X-CSRF-TOKEN": token },
    body: JSON.stringify(body)
  });
}

export default function ServiceCatalogPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [services, setServices] = useState<SalonService[]>([]);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const canManage = tenant?.membership.role === "Owner" || tenant?.membership.role === "Manager";
  const canRead = tenant?.membership.role === "Owner" ||
    tenant?.membership.role === "Manager" || tenant?.membership.role === "Receptionist";
  const activeCount = useMemo(() => services.filter(service => service.isActive).length, [services]);

  async function loadServices() {
    const response = await fetch("/api/v1/services", {
      method: "GET", credentials: "same-origin", cache: "no-store",
      referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
    });
    if (response.status === 401) { window.location.replace("/login"); return; }
    if (response.status === 403) {
      setError("Seu perfil não tem acesso ao catálogo de serviços.");
      setServices([]);
      return;
    }
    if (!response.ok) throw new Error("Não foi possível carregar os serviços do salão.");
    const body: unknown = await response.json();
    if (!Array.isArray(body)) throw new Error("A resposta do catálogo está em formato inesperado.");
    setServices(body as SalonService[]);
  }

  useEffect(() => {
    let mounted = true;
    async function initialize() {
      try {
        const response = await fetch("/api/v1/tenant", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (response.status === 401) { window.location.replace("/login"); return; }
        if (!response.ok) throw new Error("Não foi possível identificar o salão desta sessão.");
        const profile = await response.json() as TenantProfile;
        if (!mounted) return;
        setTenant(profile);
        if (profile.membership.role === "Professional") {
          window.location.replace("/my-services");
          return;
        }
        const servicesResponse = await fetch("/api/v1/services", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (servicesResponse.status === 401) { window.location.replace("/login"); return; }
        if (servicesResponse.status === 403) { setError("Seu perfil não tem acesso ao catálogo de serviços."); return; }
        if (!servicesResponse.ok) throw new Error("Não foi possível carregar os serviços do salão.");
        const body: unknown = await servicesResponse.json();
        if (!Array.isArray(body)) throw new Error("A resposta do catálogo está em formato inesperado.");
        if (mounted) setServices(body as SalonService[]);
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar o catálogo.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  function updateField<K extends keyof FormState>(field: K, value: FormState[K]) {
    setForm(current => ({ ...current, [field]: value }));
  }
  function startEditing(service: SalonService) {
    setEditingId(service.id);
    setForm({
      name: service.name, durationMinutes: String(service.durationMinutes),
      priceMinorUnits: String(service.priceMinorUnits), currency: service.currency,
      bufferBeforeMinutes: String(service.bufferBeforeMinutes),
      bufferAfterMinutes: String(service.bufferAfterMinutes),
      isActive: service.isActive ? "true" : "false"
    });
    setError(null);
    setNotice(null);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }
  function resetForm() { setEditingId(null); setForm(emptyForm); }
  function parseInteger(value: string, field: string, minimum: number, maximum: number): number {
    if (!/^-?\d+$/.test(value.trim())) throw new Error(field + ": informe um número inteiro.");
    const parsed = Number(value);
    if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum)
      throw new Error(field + ": valor fora do intervalo permitido.");
    return parsed;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(null); setNotice(null); setBusy(true);
    try {
      const payload = {
        name: form.name.trim(),
        durationMinutes: parseInteger(form.durationMinutes, "Duração", 1, 1440),
        priceMinorUnits: parseInteger(form.priceMinorUnits, "Preço", 0, Number.MAX_SAFE_INTEGER),
        currency: form.currency.trim().toUpperCase(),
        bufferBeforeMinutes: parseInteger(form.bufferBeforeMinutes, "Buffer antes", 0, 1440),
        bufferAfterMinutes: parseInteger(form.bufferAfterMinutes, "Buffer depois", 0, 1440),
        isActive: form.isActive === "true"
      };
      if (!payload.name || payload.name.length > 120 || !/^[A-Z]{3}$/.test(payload.currency) || !form.isActive)
        throw new Error("Preencha todos os campos e selecione o estado do serviço.");
      const path = editingId ? "/api/v1/services/" + editingId : "/api/v1/services";
      const response = await sendMutation(path, editingId ? "PUT" : "POST", payload);
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para alterar serviços.");
      if (response.status === 400) throw new Error("Confira nome, duração, preço, moeda e buffers informados.");
      if (!response.ok) throw new Error("Não foi possível salvar o serviço.");
      await loadServices();
      setNotice(editingId ? "Serviço atualizado." : "Serviço criado.");
      resetForm();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível salvar o serviço.");
    } finally { setBusy(false); }
  }

  async function archiveService(service: SalonService) {
    if (!window.confirm('Arquivar "' + service.name + '"? O registro será preservado e poderá ser reativado.')) return;
    setError(null); setNotice(null); setBusy(true);
    try {
      const response = await sendMutation("/api/v1/services/" + service.id, "PUT", {
        name: service.name, durationMinutes: service.durationMinutes,
        priceMinorUnits: service.priceMinorUnits, currency: service.currency,
        bufferBeforeMinutes: service.bufferBeforeMinutes,
        bufferAfterMinutes: service.bufferAfterMinutes, isActive: false
      });
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não pode arquivar serviços.");
      if (!response.ok) throw new Error("Não foi possível arquivar o serviço.");
      await loadServices();
      if (editingId === service.id) resetForm();
      setNotice("Serviço arquivado. O histórico foi preservado.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível arquivar o serviço.");
    } finally { setBusy(false); }
  }

  async function logout() {
    setBusy(true);
    try {
      const token = await getAntiforgeryToken();
      await fetch("/api/v1/auth/logout", {
        method: "POST", credentials: "same-origin", cache: "no-store",
        referrerPolicy: "no-referrer", headers: { "X-CSRF-TOKEN": token }
      });
    } finally { window.location.replace("/login"); }
  }

  return (
    <main className="workspace-shell">
      <header className="workspace-header">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">a</span>
          <span className="brand-name">atendee<span>beauty</span></span>
        </div>
        <div className="workspace-user">
          <div>
            <strong>{tenant?.name ?? "Seu salão"}</strong>
            <span>{tenant?.membership.displayName ?? ""} · {tenant?.membership.role ?? ""}</span>
          </div>
          <a className="text-button" href="/team">Equipe</a>
          <button className="text-button" type="button" onClick={logout} disabled={busy}>Sair</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">GESTÃO DO SALÃO</div>
            <h1>Serviços</h1>
            <p>Organize o catálogo, a duração e os valores dos atendimentos.</p>
          </div>
          <div className="catalog-summary"><strong>{activeCount}</strong><span>serviços ativos</span></div>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {canManage && (
          <section className="service-editor" aria-labelledby="service-editor-title">
            <div className="section-heading">
              <div>
                <h2 id="service-editor-title">{editingId ? "Editar serviço" : "Adicionar serviço"}</h2>
                <p>Informe todos os campos. O preço é armazenado como unidade monetária menor.</p>
              </div>
              {editingId && <button className="text-button" type="button" onClick={resetForm} disabled={busy}>Cancelar edição</button>}
            </div>
            <form className="service-form" onSubmit={handleSubmit}>
              <label className="field-wide">Nome do serviço
                <input value={form.name} onChange={event => updateField("name", event.target.value)}
                  maxLength={120} required disabled={busy} placeholder="Ex.: Corte de cabelo" />
              </label>
              <label>Duração (minutos)
                <input type="number" min={1} max={1440} step={1} value={form.durationMinutes}
                  onChange={event => updateField("durationMinutes", event.target.value)} required disabled={busy} />
              </label>
              <label>Preço (unidade menor)
                <input type="number" min={0} step={1} value={form.priceMinorUnits}
                  onChange={event => updateField("priceMinorUnits", event.target.value)} required disabled={busy} />
              </label>
              <label>Moeda (ISO 4217)
                <input value={form.currency} onChange={event => updateField("currency", event.target.value.toUpperCase())}
                  maxLength={3} minLength={3} required disabled={busy} placeholder="Ex.: BRL" />
              </label>
              <label>Buffer antes (min)
                <input type="number" min={0} max={1440} step={1} value={form.bufferBeforeMinutes}
                  onChange={event => updateField("bufferBeforeMinutes", event.target.value)} required disabled={busy} />
              </label>
              <label>Buffer depois (min)
                <input type="number" min={0} max={1440} step={1} value={form.bufferAfterMinutes}
                  onChange={event => updateField("bufferAfterMinutes", event.target.value)} required disabled={busy} />
              </label>
              <label>Estado
                <select value={form.isActive} onChange={event => updateField("isActive", event.target.value as FormState["isActive"])}
                  required disabled={busy}>
                  <option value="">Selecione</option>
                  <option value="true">Ativo</option>
                  <option value="false">Inativo</option>
                </select>
              </label>
              <div className="form-actions field-wide">
                <button type="submit" disabled={busy}>{busy ? "Salvando…" : editingId ? "Salvar alterações" : "Criar serviço"}</button>
              </div>
            </form>
          </section>
        )}

        <section className="service-list" aria-labelledby="service-list-title">
          <div className="section-heading">
            <div><h2 id="service-list-title">Catálogo cadastrado</h2>
              <p>{loading ? "Carregando…" : services.length + " serviço(s) no catálogo"}</p></div>
          </div>
          {loading ? <div className="empty-state">Carregando os serviços do salão…</div> :
           !canRead ? <div className="empty-state">A visualização do catálogo não está disponível para este perfil.</div> :
           services.length === 0 ? (
            <div className="empty-state"><strong>Nenhum serviço cadastrado</strong>
              <p>Quando o catálogo for preenchido, os serviços aparecerão aqui.</p></div>
           ) : (
            <div className="service-grid">
              {services.map(service => (
                <article className={"service-card" + (service.isActive ? "" : " is-inactive")} key={service.id}>
                  <div className="service-card-top">
                    <h3>{service.name}</h3>
                    <span className={"service-status" + (service.isActive ? " active" : "")}>
                      {service.isActive ? "Ativo" : "Inativo"}
                    </span>
                  </div>
                  <div className="service-price">
                    {service.priceMinorUnits.toLocaleString("pt-BR")} <span>{service.currency}</span>
                    <small>unidade monetária menor</small>
                  </div>
                  <div className="service-meta">
                    <span>{service.durationMinutes} min</span>
                    <span>Buffer {service.bufferBeforeMinutes}/{service.bufferAfterMinutes} min</span>
                  </div>
                  {canManage && <div className="service-actions">
                    <button className="secondary-button" type="button" onClick={() => startEditing(service)} disabled={busy}>Editar</button>
                    {service.isActive && <button className="archive-button" type="button"
                      onClick={() => void archiveService(service)} disabled={busy}>Arquivar</button>}
                  </div>}
                </article>
              ))}
            </div>
           )}
        </section>
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}

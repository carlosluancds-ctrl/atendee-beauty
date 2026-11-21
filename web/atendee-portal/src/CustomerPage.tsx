import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import type { Customer, Role } from "./types";

type TenantProfile = {
  tenantId: string;
  name: string;
  slug: string;
  timeZoneId: string;
  status: string;
  membership: { displayName: string; role: Role };
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
  const body = await readBody(response) as { requestToken?: string } | null;
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

export default function CustomerPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [form, setForm] = useState({ fullName: "", phoneNumber: "", emailAddress: "" });
  const [editingId, setEditingId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const canCreate = tenant?.membership.role === "Owner" || tenant?.membership.role === "Manager" || tenant?.membership.role === "Receptionist";
  const canArchive = tenant?.membership.role === "Owner" || tenant?.membership.role === "Manager";
  const isInactive = (customer: Customer) => !customer.isActive;

  async function loadCustomers() {
    const response = await fetch("/api/v1/customers", {
      method: "GET", credentials: "same-origin", cache: "no-store",
      referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
    });
    if (response.status === 401) { window.location.replace("/login"); return; }
    if (response.status === 403) {
      setError("Seu perfil não tem acesso à lista de clientes.");
      setCustomers([]);
      return;
    }
    if (!response.ok) throw new Error("Não foi possível carregar os clientes do salão.");
    const body: unknown = await response.json();
    if (body && typeof body === "object" && "items" in body && Array.isArray((body as any).items)) {
      setCustomers((body as any).items);
    } else if (Array.isArray(body)) {
      setCustomers(body as Customer[]);
    } else {
      throw new Error("A resposta da lista de clientes está em formato inesperado.");
    }
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
        await loadCustomers();
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar os clientes.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  function updateField(field: string, value: string) {
    setForm(current => ({ ...current, [field]: value }));
  }

  function startEditing(customer: Customer) {
    setEditingId(customer.id);
    setForm({
      fullName: customer.fullName,
      phoneNumber: customer.phoneNumber ?? "",
      emailAddress: customer.emailAddress ?? ""
    });
    setError(null);
    setNotice(null);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function resetForm() { setEditingId(null); setForm({ fullName: "", phoneNumber: "", emailAddress: "" }); }

  function validateForm(): string | null {
    if (!form.fullName.trim()) return "Nome é obrigatório.";
    if (form.phoneNumber && !/^\+?[1-9]\d{1,14}$/.test(form.phoneNumber.trim())) return "WhatsApp inválido.";
    if (form.emailAddress && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.emailAddress.trim())) return "E-mail inválido.";
    return null;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const validation = validateForm();
    if (validation) { setError(validation); return; }
    setError(null); setNotice(null); setBusy(true);
    try {
      const payload = {
        fullName: form.fullName.trim(),
        phoneNumber: form.phoneNumber.trim() || null,
        emailAddress: form.emailAddress.trim() || null
      };
      const path = editingId ? `/api/v1/customers/${editingId}` : "/api/v1/customers";
      const method = editingId ? "PUT" as const : "POST" as const;
      const response = await sendMutation(path, method, payload);
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para alterar clientes.");
      if (response.status === 400) throw new Error("Dados inválidos. Verifique nome e contato.");
      if (!response.ok) throw new Error("Não foi possível salvar o cliente.");
      await loadCustomers();
      setNotice(editingId ? "Cliente atualizado." : "Cliente criado.");
      resetForm();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível salvar o cliente.");
    } finally { setBusy(false); }
  }

  async function archiveCustomer(customer: Customer) {
    if (!window.confirm('Arquivar "' + customer.fullName + '"? O cliente pode ser reativado posteriormente.')) return;
    setError(null); setNotice(null); setBusy(true);
    try {
      const response = await sendMutation(`/api/v1/customers/${customer.id}/archive`, "POST", {});
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não pode arquivar clientes.");
      if (!response.ok) throw new Error("Não foi possível arquivar o cliente.");
      await loadCustomers();
      setNotice("Cliente arquivado.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível arquivar o cliente.");
    } finally { setBusy(false); }
  }

  const activeCustomers = customers.filter(c => !isInactive(c));
  const inactiveCustomers = customers.filter(c => isInactive(c));

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
          <a className="text-button" href="/services">Serviços</a>
          <button className="text-button" type="button" onClick={() => { window.location.href = "/login"; }}>Sair</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">GESTÃO DO SALÃO</div>
            <h1>Clientes</h1>
            <p>Organize a base de clientes com nome, WhatsApp e e-mail.</p>
          </div>
          <div className="catalog-summary"><strong>{activeCustomers.length}</strong><span>clientes ativos</span></div>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {canCreate && (
          <section className="customer-editor" aria-labelledby="customer-editor-title">
            <div className="section-heading">
              <div>
                <h2 id="customer-editor-title">{editingId ? "Editar cliente" : "Adicionar cliente"}</h2>
                <p>Informe nome completo e ao menos um contato.</p>
              </div>
              {editingId && <button className="text-button" type="button" onClick={resetForm} disabled={busy}>Cancelar</button>}
            </div>
            <form className="customer-form" onSubmit={handleSubmit}>
              <label className="field-wide">Nome completo
                <input value={form.fullName} onChange={event => updateField("fullName", event.target.value)}
                  maxLength={120} required disabled={busy} placeholder="Ex.: Maria Silva" />
              </label>
              <label>WhatsApp
                <input type="tel" value={form.phoneNumber} onChange={event => updateField("phoneNumber", event.target.value)}
                  disabled={busy} placeholder="+55 11 99999-9999" />
              </label>
              <label>E-mail
                <input type="email" value={form.emailAddress} onChange={event => updateField("emailAddress", event.target.value)}
                  disabled={busy} placeholder="maria@email.com" />
              </label>
              <div className="form-actions">
                <button type="submit" disabled={busy}>{busy ? "Salvando…" : editingId ? "Salvar alterações" : "Criar cliente"}</button>
              </div>
            </form>
          </section>
        )}

        <section className="customer-list" aria-labelledby="customer-list-title">
          <div className="section-heading">
            <div><h2 id="customer-list-title">Clientes cadastrados</h2>
              <p>{loading ? "Carregando…" : activeCustomers.length + " cliente(s) ativo(s)"}</p></div>
          </div>
          {loading ? <div className="empty-state">Carregando os clientes do salão…</div> :
           activeCustomers.length === 0 ? (
            <div className="empty-state"><strong>Nenhum cliente cadastrado</strong>
              <p>Clientes criados aparecerão aqui.</p></div>
           ) : (
            <div className="customer-grid">
              {activeCustomers.map(customer => (
                <article className="customer-card" key={customer.id}>
                  <div className="customer-card-top">
                    <div>
                      <h3>{customer.fullName}</h3>
                      {customer.emailAddress && <p className="customer-contact">{customer.emailAddress}</p>}
                      {customer.phoneNumber && <p className="customer-contact">{customer.phoneNumber}</p>}
                    </div>
                  </div>
                  {canArchive && <div className="customer-actions">
                    <button className="text-button secondary-button" type="button"
                      onClick={() => startEditing(customer)} disabled={busy}>Editar</button>
                  </div>}
                </article>
              ))}
            </div>
          )}
        </section>

        {inactiveCustomers.length > 0 && (
          <section className="customer-list-inactive" aria-labelledby="customer-inactive-title">
            <div className="section-heading">
              <div><h2 id="customer-inactive-title">Clientes arquivados</h2></div>
            </div>
            <div className="customer-grid">
              {inactiveCustomers.map(customer => (
                <article className="customer-card archived" key={customer.id}>
                  <div className="customer-card-top">
                    <div>
                      <h3 className="archived-name">{customer.fullName}</h3>
                    </div>
                  </div>
                  {canArchive && <div className="customer-actions">
                    <button className="text-button" type="button"
                      onClick={() => void archiveCustomer(customer)} disabled={busy}>Reativar</button>
                  </div>}
                </article>
              ))}
            </div>
          </section>
        )}
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}
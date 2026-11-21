import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import type { Customer, Professional, Role, Service } from "./types";

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

export default function AppointmentFormPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [services, setServices] = useState<Service[]>([]);
  const [professionals, setProfessionals] = useState<Professional[]>([]);
  const [filteredProfessionals, setFilteredProfessionals] = useState<Professional[]>([]);

  const [searchCustomer, setSearchCustomer] = useState("");
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null);
  const [selectedService, setSelectedService] = useState<Service | null>(null);
  const [selectedProfessional, setSelectedProfessional] = useState<Professional | null>(null);
  const [appointmentDate, setAppointmentDate] = useState<string>("");
  const [appointmentTime, setAppointmentTime] = useState<string>("09:00");

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

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

        const [customersRes, servicesRes, professionalsRes] = await Promise.all([
          fetch("/api/v1/customers", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          }),
          fetch("/api/v1/services", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          }),
          fetch("/api/v1/professionals", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          })
        ]);

        if (customersRes.ok) {
          const body: unknown = await customersRes.json();
          const list = body && typeof body === "object" && "items" in body
            ? (body as any).items as Customer[]
            : Array.isArray(body) ? body as Customer[]
            : [];
          if (mounted) setCustomers(list.filter(c => c.isActive));
        }

        if (servicesRes.ok) {
          const body: unknown = await servicesRes.json();
          const list = body && typeof body === "object" && "items" in body
            ? (body as any).items as Service[]
            : Array.isArray(body) ? body as Service[]
            : [];
          if (mounted) setServices(list.filter(s => s.isActive));
        }

        if (professionalsRes.ok) {
          const body: unknown = await professionalsRes.json();
          const list = body && typeof body === "object" && "items" in body
            ? (body as any).items as Professional[]
            : Array.isArray(body) ? body as Professional[]
            : [];
          if (mounted) setProfessionals(list.filter(p => p.isActive));
        }
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar os dados.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  const filteredCustomers = customers.filter(c =>
    c.fullName.toLowerCase().includes(searchCustomer.toLowerCase()) ||
    (c.phoneNumber && c.phoneNumber.includes(searchCustomer))
  );

  useEffect(() => {
    if (!selectedService || !selectedProfessional) {
      setFilteredProfessionals([]);
      return;
    }
    const filtered = selectedProfessional.serviceIds.includes(selectedService.id)
      ? [selectedProfessional]
      : professionals.filter(p => p.serviceIds.includes(selectedService.id));
    setFilteredProfessionals(filtered);
  }, [selectedService, selectedProfessional, professionals]);

  function updateField(field: string, value: string) {
    if (field === "appointmentDate") setAppointmentDate(value);
    else if (field === "appointmentTime") setAppointmentTime(value);
    else if (field === "searchCustomer") setSearchCustomer(value);
    else if (field === "selectedService") {
      setSelectedService(value ? JSON.parse(value) as Service : null);
      setSelectedProfessional(null);
    }
    else if (field === "selectedProfessional") {
      setSelectedProfessional(value ? professionals.find(p => p.id === value) || null : null);
    }
    else if (field === "selectedCustomer") {
      setSelectedCustomer(value ? customers.find(c => c.id === value) || null : null);
    }
  }

  function validateForm(): string | null {
    if (!selectedCustomer) return "Selecione um cliente.";
    if (!selectedService) return "Selecione um serviço.";
    if (!selectedProfessional) return "Selecione um profissional.";
    if (!appointmentDate) return "Selecione a data do agendamento.";
    if (!appointmentTime) return "Selecione a hora do agendamento.";

    const [hours, minutes] = appointmentTime.split(":").map(Number);
    const selectedDateTime = new Date(appointmentDate);
    selectedDateTime.setHours(hours || 0, minutes || 0, 0, 0);

    const now = new Date();
    if (selectedDateTime <= now) return "A data/hora deve ser futura.";

    return null;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const validation = validateForm();
    if (validation) { setError(validation); return; }
    setError(null); setNotice(null); setBusy(true);

    const [hours, minutes] = appointmentTime.split(":").map(Number);
    const startAtUtc = new Date(appointmentDate);
    startAtUtc.setHours(hours || 0, minutes || 0, 0, 0);
    const startAtUtcString = startAtUtc.toISOString();

    try {
      const payload = {
        customerId: selectedCustomer!.id,
        professionalId: selectedProfessional!.id,
        serviceId: selectedService!.id,
        startAtUtc: startAtUtcString
      };
      const response = await sendMutation("/api/v1/appointments", "POST", payload);
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para criar agendamentos.");
      if (response.status === 400) throw new Error("Dados inválidos. Verifique cliente, profissional e horário.");
      if (response.status === 409) throw new Error("Conflito de horário - o profissional já tem outro agendamento.");
      if (!response.ok) throw new Error("Não foi possível criar o agendamento.");

      setNotice("Agendamento criado com sucesso!");
      window.location.href = "/appointments";
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível criar o agendamento.");
    } finally { setBusy(false); }
  }

  const today = new Date().toISOString().split("T")[0];

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
          <button className="text-button" type="button" onClick={() => { window.location.href = "/login"; }}>Sair</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">CALENDARIO</div>
            <h1>Novo Agendamento</h1>
            <p>Crie um novo agendamento selecionando cliente, serviço e horário.</p>
          </div>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {loading ? (
          <div className="empty-state">Carregando os dados do salão…</div>
        ) : (
          <form className="appointment-form" onSubmit={handleSubmit}>
            <div className="form-section">
              <h2>Cliente</h2>
              <div>
                <input
                  type="text"
                  placeholder="Buscar cliente..."
                  value={searchCustomer}
                  onChange={e => updateField("searchCustomer", e.target.value)}
                  className="search-input"
                />
              </div>
              <select
                value={selectedCustomer?.id || ""}
                onChange={e => updateField("selectedCustomer", e.target.value)}
                disabled={busy || filteredCustomers.length === 0}
              >
                <option value="">Selecione um cliente</option>
                {filteredCustomers.length === 0 ? (
                  <option value="" disabled>Nenhum cliente encontrado</option>
                ) : (
                  filteredCustomers.map(customer => (
                    <option key={customer.id} value={customer.id}>
                      {customer.fullName} {customer.phoneNumber && `(${customer.phoneNumber})`}
                    </option>
                  ))
                )}
              </select>
            </div>

            <div className="form-section">
              <h2>Serviço</h2>
              <select
                value={selectedService?.id || ""}
                onChange={e => {
                  const service = services.find(s => s.id === e.target.value);
                  if (service) {
                    setAppointmentDate("");
                    setAppointmentTime("09:00");
                    setSelectedService(service);
                  } else {
                    setSelectedService(null);
                  }
                  setSelectedProfessional(null);
                }}
                disabled={busy || services.length === 0}
              >
                <option value="">Selecione um serviço</option>
                {services.length === 0 ? (
                  <option value="" disabled>Nenhum servico encontrado</option>
                ) : (
                  services.filter(s => s.isActive).map(service => (
                    <option key={service.id} value={service.id}>
                      {service.name} - {service.durationMinutes} min
                    </option>
                  ))
                )}
              </select>
              {selectedService && (
                <p className="service-info">
                  Duração: {selectedService.durationMinutes} minutos |
                  Preço: {(selectedService.priceMinorUnits / 100).toFixed(2)} {selectedService.currency}
                </p>
              )}
            </div>

            <div className="form-section">
              <h2>Profissional</h2>
              <select
                value={selectedProfessional?.id || ""}
                onChange={e => updateField("selectedProfessional", e.target.value)}
                disabled={busy || !selectedService || filteredProfessionals.length === 0}
              >
                <option value="">Selecione um profissional</option>
                {filteredProfessionals.length === 0 ? (
                  selectedService ? (
                    <option value="" disabled>Nenhum profissional disponível para este servico</option>
                  ) : (
                    <option value="" disabled>Selecione um servico primeiro</option>
                  )
                ) : (
                  filteredProfessionals.map(pro => (
                    <option key={pro.id} value={pro.id}>
                      {pro.displayName}
                    </option>
                  ))
                )}
              </select>
            </div>

            <div className="form-section">
              <h2>Data e Hora</h2>
              <div className="datetime-grid">
                <input
                  type="date"
                  value={appointmentDate}
                  onChange={e => updateField("appointmentDate", e.target.value)}
                  min={today}
                  disabled={busy || !selectedService || !selectedProfessional}
                />
                <input
                  type="time"
                  value={appointmentTime}
                  onChange={e => updateField("appointmentTime", e.target.value)}
                  disabled={busy || !selectedService || !selectedProfessional}
                />
              </div>
            </div>

            <div className="form-actions">
              <button type="button" className="text-button" onClick={() => { window.location.href = "/appointments"; }} disabled={busy}>
               Cancelar
              </button>
              <button type="submit" disabled={busy}>
                {busy ? "Criando..." : "Criar Agendamento"}
              </button>
            </div>
          </form>
        )}
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}
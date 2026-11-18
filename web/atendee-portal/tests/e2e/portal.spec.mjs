import { test, expect } from "@playwright/test";

const service = {
  id: "service-1", name: "Corte de cabelo", durationMinutes: 45,
  priceMinorUnits: 8500, currency: "BRL", bufferBeforeMinutes: 5,
  bufferAfterMinutes: 10, isActive: true,
  createdAtUtc: "2026-11-18T10:00:00Z", updatedAtUtc: "2026-11-18T10:00:00Z"
};
const profile = {
  id: "professional-1", identityUserId: "identity-1", displayName: "Ana Profissional",
  isActive: true, serviceIds: ["service-1"],
  createdAtUtc: "2026-11-18T10:00:00Z", updatedAtUtc: "2026-11-18T10:00:00Z"
};
const eligible = [{ identityUserId: "identity-2", displayName: "Beatriz Elegível" }];

async function json(route, body, status = 200) {
  await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
}

// These browser tests deliberately mock API responses. They validate the real
// React UI, browser navigation, request shape and role-conditioned rendering;
// they do NOT replace the backend MFA, authorization or PostgreSQL/RLS suites.
async function mockWorkspace(page, role, requests = []) {
  await page.route("**/api/v1/**", async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    requests.push({ path, method: request.method(), headers: request.headers(), body: request.postData() });
    if (path === "/api/v1/auth/antiforgery") return json(route, { requestToken: "e2e-antiforgery-token" });
    if (path === "/api/v1/tenant") {
      return json(route, {
        tenantId: "tenant-e2e", name: "Salão de Teste", slug: "salao-teste",
        timeZoneId: "America/Sao_Paulo", status: "Active",
        membership: { displayName: "Pessoa de Teste", role }
      });
    }
    if (path === "/api/v1/professionals/eligible-members") {
      return role === "Owner" || role === "Manager"
        ? json(route, eligible)
        : json(route, { error: "forbidden" }, 403);
    }
    if (path === "/api/v1/professionals/me") return json(route, profile);
    if (path === "/api/v1/professionals/me/services") return json(route, [service]);
    if (path === "/api/v1/professionals" && request.method() === "POST") {
      return json(route, {
        id: "professional-2", identityUserId: "identity-2", displayName: "Beatriz Elegível",
        isActive: true, serviceIds: [],
        createdAtUtc: "2026-11-18T10:00:00Z", updatedAtUtc: "2026-11-18T10:00:00Z"
      }, 201);
    }
    if (path === "/api/v1/professionals" && request.method() === "GET") return json(route, [profile]);
    if (path === "/api/v1/professionals/professional-1/services" && request.method() === "PUT")
      return json(route, { ...profile, serviceIds: [] });
    if (path === "/api/v1/services") return json(route, [service]);
    if (path === "/api/v1/auth/logout") return json(route, { state: "signed_out" });
    return json(route, { error: "unexpected_e2e_request", path }, 404);
  });
}

test("login renders MFA challenge and submits antiforgery-protected challenge", async ({ page }) => {
  const requests = [];
  await page.route("**/api/v1/**", async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    requests.push({ path, method: request.method(), headers: request.headers(), body: request.postData() });
    if (path === "/api/v1/auth/antiforgery") return json(route, { requestToken: "e2e-token" });
    if (path === "/api/v1/auth/login") return json(route, { state: "mfa_required" }, 202);
    if (path === "/api/v1/auth/mfa/challenge") return json(route, { state: "authenticated" });
    if (path === "/api/v1/tenant") return json(route, {
      tenantId: "tenant-e2e", name: "Salão de Teste", slug: "salao-teste",
      timeZoneId: "America/Sao_Paulo", status: "Active",
      membership: { displayName: "Gestora", role: "Manager" }
    });
    if (path === "/api/v1/services") return json(route, [service]);
    return json(route, { error: "unexpected_e2e_request" }, 404);
  });

  await page.goto("/");
  await page.getByLabel("E-mail").fill("manager@example.test");
  await page.getByLabel("Senha").fill("not-a-real-password");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Confirme sua identidade" })).toBeVisible();
  await page.getByLabel("Código de autenticação").fill("123456");
  await page.getByRole("button", { name: "Confirmar código" }).click();
  await expect(page).toHaveURL(/\/services$/);
  await expect(page.getByRole("heading", { name: "Serviços" })).toBeVisible();

  const login = requests.find(item => item.path === "/api/v1/auth/login");
  const challenge = requests.find(item => item.path === "/api/v1/auth/mfa/challenge");
  expect(login?.headers["x-csrf-token"]).toBe("e2e-token");
  expect(challenge?.headers["x-csrf-token"]).toBe("e2e-token");
});

test("first-time MFA setup requires recovery-code acknowledgement before continuing", async ({ page }) => {
  await page.route("**/api/v1/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/v1/auth/antiforgery") return json(route, { requestToken: "e2e-token" });
    if (path === "/api/v1/auth/login") return json(route, { state: "mfa_setup_required" }, 202);
    if (path === "/api/v1/auth/mfa/enrollment/start")
      return json(route, { sharedKey: "E2E-ONLY-SHARED-KEY", authenticatorUri: "otpauth://totp/e2e" });
    if (path === "/api/v1/auth/mfa/enrollment/verify")
      return json(route, { state: "mfa_enrolled", recoveryCodes: ["RECOVERY-ONE", "RECOVERY-TWO"] });
    if (path === "/api/v1/tenant") return json(route, {
      tenantId: "tenant-e2e", name: "Salão de Teste", slug: "salao-teste",
      timeZoneId: "America/Sao_Paulo", status: "Active",
      membership: { displayName: "Gestora", role: "Manager" }
    });
    if (path === "/api/v1/services") return json(route, [service]);
    return json(route, {}, 404);
  });

  await page.goto("/");
  await page.getByLabel("E-mail").fill("owner@example.test");
  await page.getByLabel("Senha").fill("not-a-real-password");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Configure sua autenticação" })).toBeVisible();
  await expect(page.getByText("E2E-ONLY-SHARED-KEY")).toBeVisible();
  await page.getByLabel("Código gerado pelo aplicativo").fill("123456");
  await page.getByRole("button", { name: "Ativar autenticação" }).click();
  await expect(page.getByRole("heading", { name: "Guarde seus códigos" })).toBeVisible();
  await expect(page.getByText("RECOVERY-ONE")).toBeVisible();
  const finish = page.getByRole("button", { name: "Concluir" });
  await expect(finish).toBeDisabled();
  await page.getByLabel("Guardei meus códigos de recuperação em local seguro.").check();
  await expect(finish).toBeEnabled();
  await finish.click();
  await expect(page).toHaveURL(/\/services$/);
});

for (const role of ["Owner", "Manager"]) {
  test(`${role} can create a Professional profile and update service assignments`, async ({ page }) => {
    const requests = [];
    await mockWorkspace(page, role, requests);
    await page.goto("/team");
    await expect(page.getByRole("heading", { name: "Equipe" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Adicionar profissional" })).toBeVisible();
    await page.getByLabel("Profissional elegível").selectOption("identity-2");
    await page.getByRole("button", { name: "Criar perfil" }).click();
    await expect(page.getByRole("heading", { name: "Beatriz Elegível" })).toBeVisible();
    await expect(page.getByRole("status")).toContainText("Perfil de Beatriz Elegível criado.");

    const anaCard = page.locator(".professional-card").filter({
      has: page.getByRole("heading", { name: "Ana Profissional" })
    });
    await anaCard.getByLabel("Corte de cabelo").uncheck();
    await anaCard.getByRole("button", { name: "Salvar serviços" }).click();
    await expect(page.getByRole("status")).toContainText("Serviços de Ana Profissional atualizados.");

    const createRequest = requests.find(item => item.path === "/api/v1/professionals" && item.method === "POST");
    expect(createRequest?.headers["x-csrf-token"]).toBe("e2e-antiforgery-token");
    expect(JSON.parse(createRequest.body)).toEqual({ identityUserId: "identity-2" });
    const assignmentRequest = requests.find(item => item.path === "/api/v1/professionals/professional-1/services");
    expect(assignmentRequest?.method).toBe("PUT");
    expect(assignmentRequest?.headers["x-csrf-token"]).toBe("e2e-antiforgery-token");
    expect(JSON.parse(assignmentRequest.body)).toEqual({ serviceIds: [] });
  });
}

test("Receptionist sees team read-only and does not request eligible memberships", async ({ page }) => {
  const requests = [];
  await mockWorkspace(page, "Receptionist", requests);
  await page.goto("/team");
  await expect(page.getByRole("heading", { name: "Equipe" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Ana Profissional" })).toBeVisible();
  await expect(page.getByText("Alterações são restritas a Owner e Manager.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Adicionar profissional" })).toHaveCount(0);
  expect(requests.some(item => item.path === "/api/v1/professionals/eligible-members")).toBe(false);
});

test("Professional sees only their own assigned services", async ({ page }) => {
  await mockWorkspace(page, "Professional");
  await page.goto("/my-services");
  await expect(page.getByRole("heading", { name: "Meus serviços" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Ana Profissional" })).toBeVisible();
  await expect(page.getByText("Corte de cabelo")).toBeVisible();
  await expect(page.getByText(/8\.500/)).toBeVisible();
  await expect(page.getByRole("heading", { name: "Equipe" })).toHaveCount(0);
});

test("Receptionist can read service catalogue but cannot see service editor", async ({ page }) => {
  await mockWorkspace(page, "Receptionist");
  await page.goto("/services");
  await expect(page.getByRole("heading", { name: "Serviços" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Catálogo cadastrado" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Adicionar serviço" })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Corte de cabelo" })).toBeVisible();
});

test("Owner can see service editor", async ({ page }) => {
  await mockWorkspace(page, "Owner");
  await page.goto("/services");
  await expect(page.getByRole("heading", { name: "Adicionar serviço" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Corte de cabelo" })).toBeVisible();
});

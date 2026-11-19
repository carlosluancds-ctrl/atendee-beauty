import { createHmac } from "node:crypto";
import { test, expect } from "@playwright/test";

function decodeBase32(secret) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let buffer = 0;
  let bits = 0;
  const bytes = [];
  for (const character of secret.replace(/=+$/g, "").replace(/\s/g, "").toUpperCase()) {
    const value = alphabet.indexOf(character);
    if (value < 0) throw new Error("Invalid base32 authenticator secret.");
    buffer = (buffer << 5) | value;
    bits += 5;
    if (bits >= 8) {
      bits -= 8;
      bytes.push((buffer >> bits) & 0xff);
    }
  }
  return Buffer.from(bytes);
}

function totp(secret) {
  const counter = BigInt(Math.floor(Date.now() / 30_000));
  const message = Buffer.alloc(8);
  message.writeBigUInt64BE(counter);
  const digest = createHmac("sha1", decodeBase32(secret)).update(message).digest();
  const offset = digest[digest.length - 1] & 0x0f;
  const value = (digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;
  return String(value).padStart(6, "0");
}

async function fillLogin(page, email, password) {
  await page.goto("/");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
}

async function loginWithPassword(page, email, password, expectedPath = "/services") {
  await fillLogin(page, email, password);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(new RegExp(expectedPath + "$"));
}

async function logout(page) {
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL(/\/login$/);
}

async function getAntiforgeryToken(page) {
  return page.evaluate(async () => {
    const response = await fetch("/api/v1/auth/antiforgery", { credentials: "same-origin" });
    if (!response.ok) throw new Error("Could not obtain antiforgery token: " + response.status);
    return (await response.json()).requestToken;
  });
}

async function postWithToken(page, path, token, body = null) {
  return page.evaluate(async ({ path, token, body }) => {
    const headers = { "X-CSRF-TOKEN": token };
    if (body !== null) headers["content-type"] = "application/json";
    const response = await fetch(path, {
      method: "POST",
      credentials: "same-origin",
      headers,
      body: body === null ? undefined : JSON.stringify(body)
    });
    const payload = await response.json().catch(() => null);
    return { status: response.status, error: payload?.error ?? null };
  }, { path, token, body });
}

async function putWithToken(page, path, token, body) {
  return page.evaluate(async ({ path, token, body }) => {
    const response = await fetch(path, {
      method: "PUT",
      credentials: "same-origin",
      headers: { "content-type": "application/json", "X-CSRF-TOKEN": token },
      body: JSON.stringify(body)
    });
    const payload = await response.json().catch(() => null);
    return { status: response.status, error: payload?.error ?? null };
  }, { path, token, body });
}

test("real API browser flow: Owner, Manager MFA enrollment, Professional and Receptionist", async ({ page, context }) => {
  test.setTimeout(120_000);
  const env = process.env;
  const required = name => {
    const value = env[name];
    if (!value) throw new Error("Missing full-stack E2E setting: " + name);
    return value;
  };

  const ownerEmail = required("ATENDEE_E2E_OWNER_EMAIL");
  const ownerPassword = required("ATENDEE_E2E_OWNER_PASSWORD");
  const ownerSecret = required("ATENDEE_E2E_OWNER_TOTP_SECRET");
  const managerEmail = required("ATENDEE_E2E_MANAGER_EMAIL");
  const managerPassword = required("ATENDEE_E2E_MANAGER_PASSWORD");
  const professionalEmail = required("ATENDEE_E2E_PROFESSIONAL_EMAIL");
  const professionalPassword = required("ATENDEE_E2E_PROFESSIONAL_PASSWORD");
  const professionalId = required("ATENDEE_E2E_PROFESSIONAL_ID");
  const receptionistEmail = required("ATENDEE_E2E_RECEPTIONIST_EMAIL");
  const receptionistPassword = required("ATENDEE_E2E_RECEPTIONIST_PASSWORD");
  const secondOwnerEmail = required("ATENDEE_E2E_SECOND_OWNER_EMAIL");
  const secondOwnerPassword = required("ATENDEE_E2E_SECOND_OWNER_PASSWORD");
  const secondOwnerSecret = required("ATENDEE_E2E_SECOND_OWNER_TOTP_SECRET");
  const secondProfessionalId = required("ATENDEE_E2E_SECOND_PROFESSIONAL_ID");

  const mutations = [];
  const eligibleRequests = [];
  page.on("request", request => {
    const url = new URL(request.url());
    if (url.pathname.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method())) {
      mutations.push({ path: url.pathname, headers: request.headers() });
    }
    if (url.pathname === "/api/v1/professionals/eligible-members")
      eligibleRequests.push(request.method());
  });

  // Capture an anonymous request token so the authenticated session can prove it is rejected.
  await page.goto("/");
  const anonymousRequestToken = await getAntiforgeryToken(page);

  // Owner uses an authenticator key persisted by the trusted first-Owner test fixture.
  await fillLogin(page, ownerEmail, ownerPassword);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Confirme sua identidade" })).toBeVisible();
  await page.getByLabel("Código de autenticação").fill(totp(ownerSecret));
  await page.getByRole("button", { name: "Confirmar código" }).click();
  await expect(page).toHaveURL(/\/services$/);

  const staleAnonymousMutation = await postWithToken(page, "/api/v1/services", anonymousRequestToken, {
    name: "Stale anonymous token",
    durationMinutes: 30,
    priceMinorUnits: 1000,
    currency: "BRL",
    bufferBeforeMinutes: 0,
    bufferAfterMinutes: 0,
    isActive: true
  });
  expect(staleAnonymousMutation).toEqual({ status: 400, error: "antiforgery_validation_failed" });

  const cookies = await context.cookies();
  const authCookie = cookies.find(cookie => cookie.name === "__Host-Atendee.Auth");
  expect(authCookie).toBeTruthy();
  expect(authCookie.secure).toBe(true);
  expect(authCookie.httpOnly).toBe(true);
  expect(authCookie.sameSite).toBe("Strict");
  expect(authCookie.path).toBe("/");

  // Create a service through the actual Owner UI and real ASP.NET API.
  await page.goto("/services");
  await page.getByLabel("Nome do serviço").fill("Corte E2E");
  await page.getByLabel("Duração (minutos)").fill("45");
  await page.getByLabel("Preço (unidade menor)").fill("8500");
  await page.getByLabel("Moeda (ISO 4217)").fill("BRL");
  await page.getByLabel("Buffer antes (min)").fill("5");
  await page.getByLabel("Buffer depois (min)").fill("10");
  await page.getByLabel("Estado").selectOption("true");
  const serviceCreateRequestPromise = page.waitForRequest(request =>
    request.url().endsWith("/api/v1/services") && request.method() === "POST");
  const serviceCreateResponsePromise = page.waitForResponse(response =>
    response.url().endsWith("/api/v1/services") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Criar serviço" }).click();
  const serviceCreateRequest = await serviceCreateRequestPromise;
  const serviceCreateResponse = await serviceCreateResponsePromise;
  if (!serviceCreateResponse.ok()) {
    const headers = await serviceCreateRequest.allHeaders();
    throw new Error("Real service-create API returned HTTP " + serviceCreateResponse.status() +
      "; request body: " + serviceCreateRequest.postData() +
      "; content-type present: " + Boolean(headers["content-type"]) +
      "; antiforgery header present: " + Boolean(headers["x-csrf-token"]) +
      "; auth cookie present: " + Boolean(headers.cookie?.includes("__Host-Atendee.Auth")) +
      "; antiforgery cookie present: " + Boolean(headers.cookie?.includes("__Host-Atendee.AntiForgery")));
  }
  await expect(page.getByRole("heading", { name: "Corte E2E" })).toBeVisible();
  const ownerServiceList = await page.evaluate(async () => {
    const response = await fetch("/api/v1/services", { credentials: "same-origin" });
    return { status: response.status, services: await response.json() };
  });
  expect(ownerServiceList.status).toBe(200);
  expect(ownerServiceList.services).toHaveLength(1);
  const ownerServiceId = ownerServiceList.services[0].id;
  expect(ownerServiceId).toBeTruthy();

  const ownerAuthenticatedRequestToken = await getAntiforgeryToken(page);

  // A second independently provisioned salon must not read or mutate Salon A resources.
  // Keep both Owner sessions alive in separate browser contexts to exercise both directions
  // without consuming extra login-rate-limit permits.
  const secondOwnerContext = await context.browser().newContext({ baseURL: "http://localhost:4176", ignoreHTTPSErrors: true });
  try {
    const secondOwnerPage = await secondOwnerContext.newPage();
    secondOwnerPage.on("request", request => {
      const url = new URL(request.url());
      if (url.pathname.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method()))
        mutations.push({ path: url.pathname, headers: request.headers() });
    });

    await fillLogin(secondOwnerPage, secondOwnerEmail, secondOwnerPassword);
    await secondOwnerPage.getByRole("button", { name: "Entrar" }).click();
    await expect(secondOwnerPage.getByRole("heading", { name: "Confirme sua identidade" })).toBeVisible();
    await secondOwnerPage.getByLabel("Código de autenticação").fill(totp(secondOwnerSecret));
    await secondOwnerPage.getByRole("button", { name: "Confirmar código" }).click();
    await expect(secondOwnerPage).toHaveURL(/\/services$/);

    const secondOwnerToken = await getAntiforgeryToken(secondOwnerPage);
    const salonBEligibleMembers = await secondOwnerPage.evaluate(async () => {
      const response = await fetch("/api/v1/professionals/eligible-members", { credentials: "same-origin" });
      return { status: response.status, members: await response.json() };
    });
    expect(salonBEligibleMembers.status).toBe(200);
    expect(salonBEligibleMembers.members.map(member => member.identityUserId)).toEqual([secondProfessionalId]);

    const salonBCreateProfessional = await postWithToken(secondOwnerPage, "/api/v1/professionals", secondOwnerToken, {
      identityUserId: secondProfessionalId
    });
    expect(salonBCreateProfessional.status).toBe(201);
    const salonBProfessionalList = await secondOwnerPage.evaluate(async () => {
      const response = await fetch("/api/v1/professionals", { credentials: "same-origin" });
      return { status: response.status, profiles: await response.json() };
    });
    expect(salonBProfessionalList.status).toBe(200);
    expect(salonBProfessionalList.profiles).toHaveLength(1);
    const salonBProfessionalId = salonBProfessionalList.profiles[0].id;
    expect(salonBProfessionalList.profiles[0].identityUserId).toBe(secondProfessionalId);

    const salonBCannotCreateProfileForSalonAIdentity = await postWithToken(
      secondOwnerPage, "/api/v1/professionals", secondOwnerToken, { identityUserId: professionalId });
    expect(salonBCannotCreateProfileForSalonAIdentity).toEqual({
      status: 400, error: "professional_membership_invalid"
    });

    const salonAEligibleMembers = await page.evaluate(async () => {
      const response = await fetch("/api/v1/professionals/eligible-members", { credentials: "same-origin" });
      return { status: response.status, members: await response.json() };
    });
    expect(salonAEligibleMembers.status).toBe(200);
    expect(salonAEligibleMembers.members.map(member => member.identityUserId)).toEqual([professionalId]);

    const salonACannotCreateProfileForSalonBIdentity = await postWithToken(
      page, "/api/v1/professionals", ownerAuthenticatedRequestToken, { identityUserId: secondProfessionalId });
    expect(salonACannotCreateProfileForSalonBIdentity).toEqual({
      status: 400, error: "professional_membership_invalid"
    });
    const salonAProfessionalListBeforeSetup = await page.evaluate(async () => {
      const response = await fetch("/api/v1/professionals", { credentials: "same-origin" });
      return { status: response.status, profiles: await response.json() };
    });
    expect(salonAProfessionalListBeforeSetup.status).toBe(200);
    expect(salonAProfessionalListBeforeSetup.profiles).toHaveLength(0);
    const salonACannotUpdateSalonBProfile = await putWithToken(
      page, "/api/v1/professionals/" + salonBProfessionalId + "/services", ownerAuthenticatedRequestToken, {
        serviceIds: [ownerServiceId]
      });
    expect(salonACannotUpdateSalonBProfile).toEqual({ status: 404, error: null });
    const secondOwnerCreate = await postWithToken(secondOwnerPage, "/api/v1/services", secondOwnerToken, {
      name: "Serviço Salão B",
      durationMinutes: 30,
      priceMinorUnits: 6000,
      currency: "BRL",
      bufferBeforeMinutes: 0,
      bufferAfterMinutes: 0,
      isActive: true
    });
    expect(secondOwnerCreate.status).toBe(201);
    const secondOwnerList = await secondOwnerPage.evaluate(async () => {
      const response = await fetch("/api/v1/services", { credentials: "same-origin" });
      return { status: response.status, services: await response.json() };
    });
    expect(secondOwnerList.status).toBe(200);
    expect(secondOwnerList.services).toHaveLength(1);
    const secondOwnerServiceId = secondOwnerList.services[0].id;
    expect(secondOwnerServiceId).toBeTruthy();
    expect(secondOwnerServiceId).not.toBe(ownerServiceId);

    const salonBReadSalonA = await secondOwnerPage.evaluate(async id =>
      (await fetch("/api/v1/services/" + id, { credentials: "same-origin" })).status, ownerServiceId);
    expect(salonBReadSalonA).toBe(404);
    const salonBUpdateSalonA = await putWithToken(secondOwnerPage,
      "/api/v1/services/" + ownerServiceId, secondOwnerToken, {
        name: "Tentativa cross-tenant B para A",
        durationMinutes: 30,
        priceMinorUnits: 6000,
        currency: "BRL",
        bufferBeforeMinutes: 0,
        bufferAfterMinutes: 0,
        isActive: true
      });
    expect(salonBUpdateSalonA).toEqual({ status: 404, error: null });

    const salonAList = await page.evaluate(async () => {
      const response = await fetch("/api/v1/services", { credentials: "same-origin" });
      return { status: response.status, services: await response.json() };
    });
    expect(salonAList.status).toBe(200);
    expect(salonAList.services).toHaveLength(1);
    expect(salonAList.services[0].id).toBe(ownerServiceId);
    const salonAReadSalonB = await page.evaluate(async id =>
      (await fetch("/api/v1/services/" + id, { credentials: "same-origin" })).status, secondOwnerServiceId);
    expect(salonAReadSalonB).toBe(404);
    const salonAUpdateSalonB = await putWithToken(page,
      "/api/v1/services/" + secondOwnerServiceId, ownerAuthenticatedRequestToken, {
        name: "Tentativa cross-tenant A para B",
        durationMinutes: 30,
        priceMinorUnits: 6000,
        currency: "BRL",
        bufferBeforeMinutes: 0,
        bufferAfterMinutes: 0,
        isActive: true
      });
    expect(salonAUpdateSalonB).toEqual({ status: 404, error: null });
  } finally {
    await secondOwnerContext.close();
  }

  await logout(page);
  const staleAuthenticatedLogout = await postWithToken(
    page, "/api/v1/auth/logout", ownerAuthenticatedRequestToken);
  expect(staleAuthenticatedLogout).toEqual({ status: 400, error: "antiforgery_validation_failed" });

  // Manager must enroll MFA before the API issues a full application session.
  await fillLogin(page, managerEmail, managerPassword);
  const enrollmentStart = page.waitForResponse(response =>
    response.url().endsWith("/api/v1/auth/mfa/enrollment/start") &&
    response.request().method() === "POST");
  await page.getByRole("button", { name: "Entrar" }).click();
  const enrollmentResponse = await enrollmentStart;
  expect(enrollmentResponse.ok()).toBe(true);
  const enrollment = await enrollmentResponse.json();
  await expect(page.getByRole("heading", { name: "Configure sua autenticação" })).toBeVisible();
  await page.getByLabel("Código gerado pelo aplicativo").fill(totp(enrollment.sharedKey));
  await page.getByRole("button", { name: "Ativar autenticação" }).click();
  await expect(page.getByRole("heading", { name: "Guarde seus códigos" })).toBeVisible();
  await page.getByLabel("Guardei meus códigos de recuperação em local seguro.").check();
  await page.getByRole("button", { name: "Concluir" }).click();
  await expect(page).toHaveURL(/\/services$/);

  // Manager creates a Professional profile and assigns the real tenant service.
  await page.goto("/team");
  await expect(page.getByRole("heading", { name: "Adicionar profissional" })).toBeVisible();
  await page.getByLabel("Profissional elegível").selectOption(professionalId);
  await page.getByRole("button", { name: "Criar perfil" }).click();
  await expect(page.getByRole("heading", { name: "Beatriz E2E" })).toBeVisible();
  const professionalCard = page.locator(".professional-card").filter({
    has: page.getByRole("heading", { name: "Beatriz E2E" })
  });
  await professionalCard.getByLabel("Corte E2E").check();
  await professionalCard.getByRole("button", { name: "Salvar serviços" }).click();
  await expect(page.getByRole("status")).toContainText("Serviços de Beatriz E2E atualizados.");
  await logout(page);

  // Professional receives only their own assigned service details from the real API.
  await loginWithPassword(page, professionalEmail, professionalPassword, "/my-services");
  await page.goto("/my-services");
  await expect(page.getByRole("heading", { name: "Meus serviços" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Beatriz E2E" })).toBeVisible();
  await expect(page.getByText("Corte E2E")).toBeVisible();
  await expect(page.getByText(/8\.500/)).toBeVisible();
  await logout(page);

  // Receptionist sees a read-only team. The UI must not request candidate discovery;
  // a direct request is still denied by the real API.
  const eligibleCountBeforeReceptionist = eligibleRequests.length;
  await loginWithPassword(page, receptionistEmail, receptionistPassword);
  await page.goto("/team");
  await expect(page.getByRole("heading", { name: "Equipe" })).toBeVisible();
  await expect(page.getByText("Alterações são restritas a Owner e Manager.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Adicionar profissional" })).toHaveCount(0);
  expect(eligibleRequests.length).toBe(eligibleCountBeforeReceptionist);
  const forbiddenStatus = await page.evaluate(async () =>
    (await fetch("/api/v1/professionals/eligible-members", { credentials: "same-origin" })).status);
  expect(forbiddenStatus).toBe(403);

  expect(mutations.length).toBeGreaterThanOrEqual(6);
  for (const mutation of mutations)
    expect(mutation.headers["x-csrf-token"], mutation.path).toBeTruthy();
});

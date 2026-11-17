# Atendee Beauty â€” backup e guia de retomada

**Snapshot validado:** 17/11/2026, 21:28 UTC
**Finalidade:** carregar este arquivo numa outra conta GPT e retomar o trabalho sem depender do histÃ³rico desta conversa.

## LocalizaÃ§Ã£o e checkpoint salvo

- RepositÃ³rio privado: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `wip/operational-identity-v1`
- Commit de cÃ³digo testado: `c815881200ea29831a6814aa45917fdf1cac3470`
- Base `main`: `44640018f6fa596755d93f9aa0e5affb713f8344`
- DiretÃ³rio do projeto no computador autorizado: `C:\Users\RDP\Documents\Atendee_Beauty_Repository_v18_Validation`
- Working tree estava limpa apÃ³s os commits/pushes desta sessÃ£o.
- As mudanÃ§as de identidade estÃ£o numa branch separada. NÃ£o foram incorporadas em `main`.

## CI â€” identidade e persistÃªncia aprovadas

<badge>GitHub Actions: SUCCESS</badge>

Workflow: PostgreSQL 18 persistence integration
Run: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44179994461
Commit testado: `c815881200ea29831a6814aa45917fdf1cac3470`

Resultados da execuÃ§Ã£o:
- .NET locked restore: PASS.
- Build Release: PASS, 0 warnings e 0 errors.
- Domain checks: **15/15**.
- PostgreSQL 18.7 (Debian 18.7-1.pgdg12+2): inicializaÃ§Ã£o e health check PASS.
- Provisionamento de database, papÃ©is e migrations 001 + 002: PASS.
- EF Core + Npgsql + RLS + pool: **9/9**.
- Operational Identity + TOTP + tenant context: **23/23**.
- RemoÃ§Ã£o do container e volume descartÃ¡veis: PASS.
- Nenhuma etapa crÃ­tica de teste foi ignorada.

### O que os 23 testes de identidade verificaram

- Mapeamento Identity schema v2, sem tabelas WebAuthn/passkey prematuras.
- CriaÃ§Ã£o de login sintÃ©tico e validaÃ§Ã£o de senha certa/errada.
- Segredo TOTP cifrado em repouso e descriptografado pelo Data Protection ao ser lido pelo Identity.
- TOTP RFC 6238 vÃ¡lido; cÃ³digo rejeitado na segunda utilizaÃ§Ã£o.
- Duas requisiÃ§Ãµes concorrentes com o mesmo intervalo TOTP: somente uma Ã© aceita.
- Dez recovery codes; conteÃºdo cifrado no banco, uso Ãºnico e replay recusado.
- Resolver retorna a Ãºnica associaÃ§Ã£o ativa do usuÃ¡rio.
- Papel auth nÃ£o possui SELECT direto em `tenant_users` nem `tenants`.
- FunÃ§Ã£o de resoluÃ§Ã£o nÃ£o retorna membership sem identidade definida transacionalmente.
- Header/query/claims antigos nÃ£o conseguem substituir o tenant confiÃ¡vel.
- Ator e papel vÃªm do registro persistido no servidor.
- Constraint do banco impede a mesma identidade de entrar em um segundo salÃ£o.

## HistÃ³rico dos bloqueios corrigidos

- Run `44178869368`: o teste nÃ£o passava o novo `UserManager` ao middleware. Assinatura/claim MFA do teste corrigidas.
- Run `44179269656`: o container de testes nÃ£o registrava serviÃ§os de autenticaÃ§Ã£o necessÃ¡rios ao `SignInManager`. `AddAuthentication().AddIdentityCookies()` foi adicionado ao setup de teste.
- Run `44179605495`: helper de teste criava o segundo salÃ£o sem fazer `SET LOCAL ROLE atendee_owner`. Corrigido.
- Run `44179994461`: passou integralmente, com todos os testes acima.

## O que foi salvo nesta branch

1. ASP.NET Core Identity para contas operacionais dos salÃµes no schema PostgreSQL `identity`.
2. SeparaÃ§Ã£o do Administrador global Atendee: nenhuma identidade global foi criada dentro do store operacional.
3. RestriÃ§Ã£o de um usuÃ¡rio operacional para exatamente um salÃ£o (`UNIQUE(identity_user_id)`).
4. Email normalizado globalmente Ãºnico.
5. Tenant resolvido no servidor a cada requisiÃ§Ã£o pelo `NameIdentifier` do cookie.
6. FunÃ§Ã£o PostgreSQL `SECURITY DEFINER` sem argumento de tenant/usuÃ¡rio, usando contexto `app.identity_user_id` local Ã  transaÃ§Ã£o.
7. Papel `atendee_auth_login` sem SELECT direto em `tenant_users` ou `tenants`, com acesso apenas Ã  funÃ§Ã£o controlada.
8. TOTP RFC 6238, segredo e recovery codes cifrados com ASP.NET Core Data Protection no EF value converter.
9. Anti-replay de TOTP em `identity.TotpReplayGuards`, com atualizaÃ§Ã£o condicional atÃ´mica.
10. Owner/Manager exigem MFA; sessÃ£o normal inclui `amr=mfa`, tambÃ©m verificado apÃ³s promoÃ§Ã£o de papel.
11. Cookies `Secure`, `HttpOnly`, `SameSite=Strict`, prefixo `__Host-`; cookie temporÃ¡rio separado para setup MFA.
12. Antiforgery para mÃ©todos mutÃ¡veis e rate limits por IP nas rotas de autenticaÃ§Ã£o.
13. Migration manual `002_create_operational_identity.sql`, teste de integraÃ§Ã£o `Atendee.Identity.Checks` e CI PostgreSQL 18.7 ampliado.

## Arquivos principais

- `src/Atendee.Api/Program.cs` â€” cookies, CSRF, rate limiting e pipeline.
- `src/Atendee.Api/Authentication/OperationalAuthenticationEndpoints.cs` â€” login, MFA, logout e perfil.
- `src/Atendee.Infrastructure/Identity/AtendeeIdentityDbContext.cs` â€” EF Identity e criptografia dos tokens.
- `src/Atendee.Infrastructure/Identity/OperationalTotpTokenProvider.cs` â€” TOTP e anti-replay.
- `src/Atendee.Infrastructure/Identity/PostgresTenantMembershipResolver.cs` â€” resolver restrito.
- `src/Atendee.Infrastructure/Web/TenantContextMiddleware.cs` â€” resoluÃ§Ã£o de contexto por requisiÃ§Ã£o.
- `src/Atendee.Infrastructure/Web/TrustedRequestContext.cs` â€” contexto scoped de ator/tenant.
- `db/security/001_roles_template.sql` â€” papÃ©is do banco.
- `db/migrations/002_create_operational_identity.sql` â€” Identity, membership e funÃ§Ã£o SQL.
- `tests/Atendee.Identity.Checks/Program.cs` â€” 23 checks de integraÃ§Ã£o de identidade.
- `.github/workflows/postgres18-persistence.yml` â€” CI completa com PostgreSQL 18.7.
- `docs/technical/OPERATIONAL_IDENTITY_DESIGN_V1.md` â€” arquitetura, limites e riscos.
- `docs/technical/SECURITY_BOUNDARIES.md` â€” limites da seguranÃ§a atual.

## Limites ainda existentes â€” nÃ£o declarar produÃ§Ã£o pronta

- CI verde valida os testes existentes, mas nÃ£o Ã© certificaÃ§Ã£o de seguranÃ§a nem aprovaÃ§Ã£o de deploy.
- NÃ£o fazer deploy nem usar dados reais nesta etapa.
- Signup pÃºblico, verificaÃ§Ã£o de email, convites e reset de senha continuam desligados atÃ© escolha de provedor de e-mail e desenho do fluxo.
- Administrador global Atendee permanece separado e ainda nÃ£o foi implementado.
- WebAuthn/passkeys foram adiados para migration futura; Identity Schema v2 estÃ¡ explicitamente selecionado.
- Data Protection fora de Development exige key ring persistente/protegido. O cÃ³digo atual permite produÃ§Ã£o Windows com DPAPI; nÃ£o-Windows falha fechado atÃ© configurar proteÃ§Ã£o por certificado/KMS/serviÃ§o gerenciado.
- Rate limit Ã© em memÃ³ria e por IP; produÃ§Ã£o multi-instÃ¢ncia/proxy requer trusted forwarded headers e limiter distribuÃ­do.
- Implementar matriz de acesso por status do tenant antes de endpoints de negÃ³cio: PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing e Closed.
- Completar testes HTTP de cookie/session, CSRF, revogaÃ§Ã£o, conta desativada, MFA bypass, enumeraÃ§Ã£o, auditoria e autorizaÃ§Ã£o por papel.
- O GUC PostgreSQL pode ser definido por uma sessÃ£o SQL. `app.identity_user_id` sÃ³ Ã© confiÃ¡vel porque o backend o deriva do cookie autenticado, usa SQL parametrizado e define contexto transaction-local. NÃ£o protege contra SQL injection/execuÃ§Ã£o arbitrÃ¡ria.
- Rever rotaÃ§Ã£o/revogaÃ§Ã£o de sessÃµes e procedimentos de recuperaÃ§Ã£o MFA antes de produÃ§Ã£o.

## PrÃ³ximos passos recomendados

1. Confirmar checkout da branch `wip/operational-identity-v1` no commit `c815881200ea29831a6814aa45917fdf1cac3470`.
2. Fazer revisÃ£o humana do fluxo login/MFA/cookies e dos SQLs; CI jÃ¡ passou.
3. Acrescentar testes HTTP end-to-end para antiforgery, cookies, MFA setup/challenge, logout, revogaÃ§Ã£o, desativaÃ§Ã£o e enumeraÃ§Ã£o.
4. Implementar a matriz de autorizaÃ§Ã£o por role e tenant status, e auditoria de eventos.
5. Definir provedor de email e sÃ³ depois ativar email verification, password reset e invitations.
6. Implementar store/fluxo separado de Platform Admin, MFA forte e break-glass auditado.
7. Rever DP/KMS, backup/restore, forwarded headers, CORS e rate limiter distribuÃ­do antes de qualquer produÃ§Ã£o.
8. SÃ³ depois abrir PR da branch para `main`; nÃ£o fazer merge automÃ¡tico nem deploy.

## InstruÃ§Ã£o pronta para a prÃ³xima conta GPT

> Leia este guia e `docs/technical/OPERATIONAL_IDENTITY_DESIGN_V1.md`. Conecte ao repositÃ³rio privado `carlosluancds-ctrl/atendee-beauty`, faÃ§a checkout de `wip/operational-identity-v1` no commit `c815881200ea29831a6814aa45917fdf1cac3470` e confira a CI `44179994461`. O gate de persistÃªncia e identidade estÃ¡ verde (15 domÃ­nio, 9 persistÃªncia, 23 identidade). Continue com revisÃ£o de seguranÃ§a e testes HTTP/sessÃ£o/CSRF/revogaÃ§Ã£o/auditoria. Mantenha o trabalho em branch separada, nÃ£o sobrescreva `main`, nÃ£o habilite signup e nÃ£o faÃ§a deploy.

---
Backup criado em 17/11/2026. Este documento Ã© um handoff de trabalho e nÃ£o substitui revisÃ£o de seguranÃ§a independente.

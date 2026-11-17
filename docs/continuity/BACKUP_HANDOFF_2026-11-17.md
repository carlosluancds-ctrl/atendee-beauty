# Atendee Beauty â€” backup e guia de retomada

**Snapshot:** 17/11/2026, 21:16 UTC
**PropÃ³sito:** carregar este arquivo em outra conta GPT e continuar o trabalho sem depender do histÃ³rico desta conversa.

## Estado salvo

- RepositÃ³rio GitHub privado: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch de trabalho (nÃ£o Ã© `main`): `wip/operational-identity-v1`
- Commit do snapshot: `a74089cc1c325d1e7130bcaca6953797ee00d745`
- Branch base: `main`
- Commit base da branch: `4464001` â€” registro do gate PostgreSQL 18.7
- DiretÃ³rio no computador autorizado: `C:\Users\RDP\Documents\Atendee_Beauty_Repository_v18_Validation`
- Todas as alteraÃ§Ãµes feitas nesta etapa foram commitadas localmente. Working tree estava limpa depois do commit.
- A etapa de identidade ainda **nÃ£o foi publicada em main nem aprovada pelo CI PostgreSQL** quando este backup foi criado.

## ValidaÃ§Ãµes comprovadas

### Gate anterior â€” aprovado em GitHub Actions

Workflow: PostgreSQL 18 persistence integration
Run: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44166541043
Commit: `6925d2add40544719b7cf16101665b333185b361`

- .NET locked restore: PASS
- Build Release: PASS, 0 warnings / 0 errors
- Domain checks: 15/15
- PostgreSQL 18.7 disposable container: PASS
- EF Core + Npgsql + RLS + pool integration: 9/9
- Cleanup do banco descartÃ¡vel: PASS
- Nenhum teste de integraÃ§Ã£o foi ignorado nessa execuÃ§Ã£o.

A primeira tentativa (run 44166048437) falhou na criaÃ§Ã£o do schema porque `atendee_owner` nÃ£o possuÃ­a `CREATE` no database. Foi corrigida: o administrador cria o schema e atribui sua propriedade ao papel dedicado sem ampliar os privilÃ©gios do owner.

### Etapa de identidade â€” validaÃ§Ã£o local parcial

Executado depois da implementaÃ§Ã£o inicial:

- `dotnet restore Atendee.sln --locked-mode`: PASS
- `dotnet build Atendee.sln --no-restore --configuration Release`: PASS, 0 warnings / 0 errors
- `Atendee.Domain.Checks`: PASS, 15/15

**Ainda nÃ£o comprovado:** executar a migration `002_create_operational_identity.sql` num PostgreSQL 18.7 descartÃ¡vel, correr `Atendee.Identity.Checks` de ponta a ponta e obter CI verde para esta branch. O workflow foi ampliado para esse teste, mas o novo run nÃ£o tinha sido executado/inspecionado quando o snapshot foi feito.

## O que foi implementado nesta branch

1. ASP.NET Core Identity para contas operacionais dos salÃµes no schema PostgreSQL `identity`.
2. SeparaÃ§Ã£o conceitual do Administrador global Atendee: nenhuma identidade global foi criada dentro do store operacional.
3. RestriÃ§Ã£o de um usuÃ¡rio operacional por um Ãºnico salÃ£o (`UNIQUE(identity_user_id)`).
4. Unicidade global do email normalizado.
5. ResoluÃ§Ã£o server-side do tenant a cada requisiÃ§Ã£o usando o `NameIdentifier` do cookie autenticado.
6. FunÃ§Ã£o PostgreSQL `SECURITY DEFINER` sem parÃ¢metro de tenant/usuÃ¡rio, usando contexto `app.identity_user_id` limitado Ã  transaÃ§Ã£o.
7. Papel `atendee_auth_login` sem SELECT direto em `atendee.tenant_users` e `atendee.tenants`; sÃ³ pode executar a funÃ§Ã£o de consulta restrita.
8. TOTP RFC 6238, chave e recovery codes protegidos com ASP.NET Core Data Protection no EF value converter.
9. ProteÃ§Ã£o contra replay TOTP numa tabela separada `identity.TotpReplayGuards`, com UPDATE condicional atÃ´mico.
10. Owner e Manager exigem TOTP. A sessÃ£o normal precisa conter `amr=mfa`; o middleware verifica isso tambÃ©m quando o usuÃ¡rio Ã© promovido a papel privilegiado.
11. Cookies `Secure`, `HttpOnly`, `SameSite=Strict`, prefixo `__Host-`; setup MFA em cookie separado e curto.
12. Antiforgery global para mÃ©todos mutÃ¡veis e rate limit por IP para rotas de autenticaÃ§Ã£o.
13. MigraÃ§Ã£o SQL manual `002_create_operational_identity.sql` e runner de integraÃ§Ã£o `tests/Atendee.Identity.Checks`.
14. Workflow GitHub atualizado para provisionar papÃ©is separados, executar as duas migrations e rodar os testes de identidade.

## Arquivos principais

- `src/Atendee.Api/Program.cs` â€” configuraÃ§Ã£o da API, cookies, CSRF, limites e pipeline.
- `src/Atendee.Api/Authentication/OperationalAuthenticationEndpoints.cs` â€” login, desafio MFA, inscriÃ§Ã£o MFA, logout e perfil autenticado.
- `src/Atendee.Infrastructure/Identity/AtendeeIdentityDbContext.cs` â€” EF Identity e criptografia dos valores de token.
- `src/Atendee.Infrastructure/Identity/OperationalTotpTokenProvider.cs` â€” TOTP e proteÃ§Ã£o anti-replay.
- `src/Atendee.Infrastructure/Identity/PostgresTenantMembershipResolver.cs` â€” resoluÃ§Ã£o restrita do vÃ­nculo.
- `src/Atendee.Infrastructure/Web/TenantContextMiddleware.cs` â€” tenant/role confiÃ¡veis por requisiÃ§Ã£o.
- `src/Atendee.Infrastructure/Web/TrustedRequestContext.cs` â€” contexto de ator e tenant scoped.
- `db/security/001_roles_template.sql` â€” papÃ©is do banco.
- `db/migrations/002_create_operational_identity.sql` â€” schema Identity, memberships e funÃ§Ã£o de resoluÃ§Ã£o.
- `tests/Atendee.Identity.Checks/Program.cs` â€” testes para criptografia, TOTP, recovery codes, replay concorrente, privilÃ©gios e contexto.
- `.github/workflows/postgres18-persistence.yml` â€” CI PostgreSQL 18.7 com as novas verificaÃ§Ãµes.
- `docs/technical/OPERATIONAL_IDENTITY_DESIGN_V1.md` â€” desenho de arquitetura e riscos.
- `docs/technical/SECURITY_BOUNDARIES.md` â€” limites de seguranÃ§a atuais.

## Riscos/limitaÃ§Ãµes para nÃ£o esquecer

- NÃ£o considerar a aplicaÃ§Ã£o pronta para produÃ§Ã£o.
- NÃ£o fazer deploy, nÃ£o criar dados reais e nÃ£o habilitar signup pÃºblico.
- Email verification, password-reset, convite e envio de recuperaÃ§Ã£o dependem da escolha de um provedor de e-mail; esses fluxos permanecem desligados.
- Administrador Atendee global permanece separado e nÃ£o foi implementado ainda.
- WebAuthn/passkeys foram adiados; Identity Schema v2 foi selecionado explicitamente e a migration atual nÃ£o inclui tabelas de passkey.
- Data Protection fora de Development requer key ring persistente e proteÃ§Ã£o explÃ­cita. O cÃ³digo atual sÃ³ permite produÃ§Ã£o Windows com DPAPI; Linux falha fechado atÃ© configurar proteÃ§Ã£o via certificado/KMS/managed key.
- O rate limit da API estÃ¡ em memÃ³ria e por IP. Para produÃ§Ã£o multi-instÃ¢ncia/proxy, configurar forwarded headers confiÃ¡veis e limiter distribuÃ­do.
- Middleware sÃ³ bloqueia tenant em `Closed`; implementar a matriz de polÃ­ticas para PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing e Closed antes de criar endpoints de negÃ³cio.
- Completar testes de sessÃ£o/revogaÃ§Ã£o, cookies, CSRF, MFA bypass, disablement, enumeraÃ§Ã£o, audit log e autorizaÃ§Ã£o por papel.
- Custom PostgreSQL GUCs podem ser definidos pela prÃ³pria sessÃ£o. `app.identity_user_id` Ã© confiÃ¡vel apenas porque o backend o deriva do cookie autenticado, usa SQL parametrizado e define o valor transaction-local. NÃ£o Ã© defesa contra SQL injection ou execuÃ§Ã£o arbitrÃ¡ria de SQL.
- Rever o fluxo de rotaÃ§Ã£o/revogaÃ§Ã£o de sessÃ£o e os testes de MFA com cuidado antes de deploy.

## PrÃ³xima sequÃªncia recomendada

1. Verificar que o branch/commit Ã© exatamente `wip/operational-identity-v1` / `a74089c`.
2. Rodar o workflow GitHub Actions da branch e acompanhar o run.
3. Corrigir erros de migration, permissÃµes ou integraÃ§Ã£o atÃ© todos os checks passarem sem SKIP.
4. Examinar os logs de teste e verificar especialmente:
   - TOTP secreto e recovery codes cifrados em repouso;
   - mesmo cÃ³digo TOTP nÃ£o pode ser reutilizado nem por duas requisiÃ§Ãµes concorrentes;
   - papel auth nÃ£o lÃª diretamente `tenants` nem `tenant_users`;
   - funÃ§Ã£o nÃ£o retorna membership sem contexto local autenticado;
   - contexto nÃ£o pode ser trocado por header, query string ou claims antigas;
   - usuÃ¡rio nÃ£o pode ter segunda associaÃ§Ã£o a outro salÃ£o;
   - o pool de tamanho 1 nÃ£o retÃ©m o GUC entre transaÃ§Ãµes.
5. Depois de CI verde, pedir revisÃ£o do cÃ³digo e dos SQLs. Somente entÃ£o abrir PR para `main`; nÃ£o fazer merge automÃ¡tico.
6. Implementar testes da camada HTTP, sessÃ£o, CSRF, revogaÃ§Ã£o, MFA bypass e auditoria.
7. Selecionar provedor de e-mail antes de ativar verificaÃ§Ã£o, convites e reset de senha.

## InstruÃ§Ã£o para a prÃ³xima conta GPT

> Abra este guia, conecte ao repositÃ³rio privado `carlosluancds-ctrl/atendee-beauty` se a conta tiver acesso, faÃ§a checkout de `wip/operational-identity-v1` no commit `a74089cc1c325d1e7130bcaca6953797ee00d745`, e continue a validaÃ§Ã£o da migration/CI PostgreSQL 18.7. NÃ£o sobrescreva `main`, nÃ£o faÃ§a deploy, nÃ£o habilite cadastro pÃºblico e nÃ£o assuma que a etapa de identidade estÃ¡ aprovada. Leia primeiro `docs/technical/OPERATIONAL_IDENTITY_DESIGN_V1.md` e este guia. O prÃ³ximo resultado que precisa ser obtido Ã© CI completo verde sem SKIP.

---
Este arquivo registra o estado de trabalho em 17/11/2026. Ele nÃ£o Ã© um certificado de seguranÃ§a nem substitui testes/revisÃ£o.

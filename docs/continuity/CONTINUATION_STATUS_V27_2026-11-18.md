# Atendee Beauty — continuidade V27 (2026-11-18)

## Estado do repositório
- Branch: security/csrf-webhook-antiforgery-v23
- Base mantida: wip/operational-identity-v1
- main permanece intocada.
- Este incremento adiciona a primeira trilha persistente de auditoria para operações de segurança de identidade.
- Nenhum endpoint administrativo foi exposto e nenhum deploy foi executado.

## V27 — trilha persistente de auditoria

### Implementado
- Nova abstração ISecurityAuditWriter e implementação PostgreSQL PostgresSecurityAuditWriter.
- Novo modelo identity."SecurityAuditEvents" na migração 003_create_security_audit_events.sql.
- Eventos atualmente permitidos:
  - operational_account.sessions_revoked
  - operational_account.disabled
  - operational_mfa.enrolled
  - operational_mfa.totp_challenge_succeeded
  - operational_mfa.recovery_code_redeemed
- Cada evento armazena somente:
  - ID do evento;
  - tipo allowlisted;
  - ator (quando houver contexto autenticado confiável);
  - identidade alvo;
  - timestamp UTC gerado no servidor;
  - trace ID de correlação, quando presente.
- Nenhuma senha, token, TOTP, recovery code, corpo HTTP, e-mail ou endereço IP é persistido pelo writer.
- A alteração da security stamp e o evento de auditoria são gravados na mesma transação PostgreSQL da identidade. Falha na escrita do evento reverte a alteração da conta/sessão.
- atendee_auth_login recebeu somente INSERT na tabela. SELECT, UPDATE, DELETE e TRUNCATE não são concedidos ao runtime.
- Trigger impede UPDATE, DELETE e TRUNCATE, inclusive como defesa adicional além dos privilégios.
- Workflow PostgreSQL 18.7 aplica a migração 003 antes dos testes.

### Cobertura adicionada
- Verifica que revogação de sessões e desativação produzem eventos persistidos.
- Verifica que o papel de runtime não consegue reescrever eventos.
- Build Release: sem warnings/erros.\n- A primeira execu??o encontrou somente um defeito no helper de teste (reader Npgsql ainda aberto no commit da transa??o); o helper foi corrigido para descartar o reader antes do commit e a execu??o subsequente passou.
- Domain checks: 15/15.
- Os testes HTTP/PostgreSQL precisam ser confirmados pelo CI após o push; o ambiente remoto local não tem Docker disponível.

### Limites explícitos
- A trilha cobre somente as duas operações de segurança acima. Login bem-sucedido/falho, MFA enrollment/challenge/recovery, mudanças de membership e ações administrativas ainda não são auditados.
- O schema atual não registra tenant ID. A resolução de tenant é separada da identidade e não há ainda um fluxo administrativo confiável para atribuir um tenant a essas ações.
- Não há consulta administrativa ou exportação de auditoria.
- A trilha é append-only para os papéis da aplicação. Um administrador de banco com poderes de proprietário/superusuário ainda pode alterar o schema ou desabilitar triggers; isso não é um ledger inviolável/WORM.
- IOperationalAccountSecurityService continua sem endpoint público. Não expor até que autorização administrativa, MFA e política de auditoria estejam concluídas.

## Próximos passos
1. Confirmar migração e testes HTTP no CI PostgreSQL 18.7.
2. Expandir eventos para MFA e membership, com gravação transacional.
3. Projetar matriz de autorização por papel e testes negativos Owner/Manager/Receptionist/Professional.
4. Só então projetar comandos administrativos de disable/revoke.
5. Continuar pendências anteriores: Platform Admin MFA/break-glass, WebAuthn/passkeys, email provider, rate limiting distribuído/trusted proxy, Data Protection em ambiente não Windows, restore drill e revisão independente.

## Guardrails mantidos
- Não alterar main.
- Não habilitar signup.
- Não implantar em produção.
- Não expor endpoints administrativos de desativação/revogação.
- Não definir automaticamente a política de acesso para estados Trial, PastDue, Suspended, Closing ou Closed; essa decisão continua aguardando confirmação do produto.

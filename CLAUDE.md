# Atendee Beauty - instruções para a IA que retomar este projeto

Quando o usuário disser "continue" (ou "Continue esse projeto") e fornecer o link deste repositório, siga este arquivo.

## 1. Onde está o trabalho
- A branch padrão `main` NÃO contém o trabalho recente e NUNCA deve ser alterada.
- O trabalho ativo está na branch `security/csrf-webhook-antiforgery-v23` (base `wip/operational-identity-v1`). Faça checkout dela antes de qualquer coisa.
- Se você só enxerga a `main`, peça acesso à branch acima ou o link direto dela; não simule continuidade.

## 2. Leia, nesta ordem
1. `docs/continuity/CONTINUE_HERE.md` (estado, decisões abertas/aprovadas, próximo passo, rotina, guardrails).
2. O `docs/continuity/CONTINUATION_STATUS_V*.md` de MAIOR número (detalhes do último incremento, CI observado, limitações).
3. Conforme a tarefa: `docs/product/POLITICA_ACESSO_STATUS_TENANT_V1.md` (aprovada pelo proprietário), `docs/technical/AUTHORIZATION_MATRIX_V1.md`, `docs/technical/ADMIN_ACTIONS_DESIGN_V1.md` (proposta, nada implementado).

## 3. Verifique o estado real antes de agir
`git status --short --branch`, `git log --oneline -10`, HEAD local x remoto e o CI mais recente da branch. Não presuma que o HEAD ou o CI são os descritos nos documentos; eles podem estar defasados.

## 4. Como trabalhar
- Incrementos pequenos e testáveis, com testes negativos. Inspecione o código antes de alterar.
- `git diff --check` e build Release antes do commit. Commit e push SOMENTE na branch de trabalho.
- Acompanhe o CI do SHA exato até concluir e confirme no log que as novas asserções rodaram. Não declare sucesso sem observar.
- Ao fim de cada etapa: atualize `CONTINUE_HERE.md` e crie/atualize o `CONTINUATION_STATUS_V<n>`, faça push e deixe a working tree limpa. Assim qualquer nova conta consegue continuar.
- Perguntar ao usuário SOMENTE para: decisão de produto ou política, mudança de escopo, credencial/permissão nova, ação irreversível. Não repita perguntas já decididas.

## 5. Guardrails absolutos
Não alterar `main`. Não fazer deploy. Não habilitar signup público. Não criar endpoints administrativos de disable/revoke/gestão cross-tenant antes de autorização administrativa, MFA e auditoria aprovadas. Não confiar em tenant, papel, status ou ator vindos do cliente. Não registrar senhas, tokens, TOTP, recovery codes, segredos, cookies ou corpos de requisição. Não chamar o ledger de auditoria de imutável contra DB owner/superuser. CI verde não é aprovação para produção. Não alterar a matriz de status do tenant aprovada nem decidir os itens em aberto (PastDue, retenção pós-Closed, quem altera status, modelo de Platform Admin etc.) sem confirmação explícita do proprietário. Nunca peça nem exponha tokens no chat.

## 6. Acesso técnico (histórico, não garantido)
O trabalho vinha sendo feito numa máquina Windows (`runnervm9zap3`) via Desktop Commander + GitHub CLI. Use os mecanismos de acesso autorizados disponíveis no novo ambiente. Dicas: processos do Desktop Commander retornam em ~3 s (consulte com read_process_output); no PowerShell 5 use BOM UTF-8 em scripts com acentos e caminhos absolutos em APIs .NET; evite `--jq` complexo no `gh`.

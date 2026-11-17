# Preparação do repositório privado — v1

Data: 17/11/2026

## Escopo

Consolidar o Starter Kit v7 revisado na raiz do repositório, para que o workflow GitHub Actions use paths relativos à raiz. Preservar no mesmo repositório as especificações de produto, arquitetura, segurança, permissões e continuidade do backup v17.

## Segurança da publicação

Verificação estática do pacote de origem:
- Nenhum arquivo `.env`, `.pem`, `.pfx`, segredo ou credencial foi encontrado.
- Nenhuma chave privada ou token GitHub com formato reconhecível foi encontrado.
- `appsettings.json` contém apenas configuração de logging.
- Arquivos de papéis SQL declaram explicitamente que senhas e credenciais não devem ser armazenadas no código-fonte.
- As referências a `secret`, `token` e `password` nos arquivos são documentação, nomes de parâmetros ou geração de senhas efêmeras de CI; não são valores de credenciais reais.

## Validação e limitações

- Estrutura de diretórios e paths do workflow preparados na raiz.
- O conteúdo técnico é o Starter Kit v7 revisado; a consolidação documental não muda a versão funcional do código.
- YAML, Bash e scripts passaram pela revisão estática registrada em `docs/technical/CI_WORKFLOW_REVIEW_V1.md` e `docs/product/RESULTADO_REVISAO_CI_POSTGRES18_V17.txt`.
- .NET build v7 e PostgreSQL 18.7/RLS/pool em runtime continuam pendentes.
- Este relatório não declara a CI aprovada e não autoriza deploy.

## Privacidade e governança

O repositório deve ser criado como **privado**, sem licença pública, e sem inicialização automática de README, `.gitignore` ou licença, pois o primeiro commit já contém esses arquivos.

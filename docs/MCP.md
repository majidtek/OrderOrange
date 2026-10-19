# OrderOrange MCP server and tools API

The API hosts a **Model Context Protocol** server so language models and agents (Grok, Claude,
Claude Code, Cursor, any MCP client) can read the platform's live data — SQL Server and
MongoDB — through a curated, read-only tool catalogue. The same tools are also exposed as a
plain REST API with an OpenAPI description.

| | URL (staging) | URL (production, when deployed) |
|---|---|---|
| MCP endpoint (Streamable HTTP, JSON-RPC 2.0) | `https://orderorange.com:8600/mcp` | `https://api.orderorange.com/mcp` |
| Discovery | `/api/mcp` | same |
| Tools as REST | `POST /api/mcp/tools/{name}` | same |
| OpenAPI 3.1 | `/api/mcp/openapi.json` | same |

## Authentication and scopes

Every call needs `Authorization: Bearer <token>`. Three kinds of bearer:

| Bearer | Sees | Writes |
|---|---|---|
| `Mcp:Token` (appsettings, long random secret) | whole platform | per `Mcp:AllowWrites` |
| Administrator JWT (admin app login) | whole platform | no |
| Store-scoped token `oo_scope.<payload>.<hmac>` | one store | per token |

Store-scoped tokens are minted by the API itself (`McpEndpoint.MintStoreToken`) for the
**partner assistant**: ten-minute life, HMAC-signed with `Mcp:Token`, payload
`{s: storeId, w: writes, e: expiry}`. For such a caller every `storeId` argument is pinned
to its own store, `get_order` refuses other stores' orders, `set_item_availability` only
touches its own items, and the platform-wide tools (`sql_query`, `mongo_*`, `schema`,
`platform_overview`, `list_stores`, `visitors`) are hidden from `tools/list` and refused.
No `Mcp:Token` configured = the server answers 503 and MCP is off.

## Configuration (appsettings.json)

```json
"Mcp":  { "Token": "<long random secret>", "AllowWrites": false },
"Grok": { "ApiKey": "xai-…", "Model": "grok-4.7", "Reasoning": "low", "AnalystReasoning": "medium", "MaxPerIpPerHour": 60 }
```

`AllowWrites` gates the two write tools. Keep it `false` unless an agent must flip store
open/closed or item availability.

## Tools

Read-only, typed (preferred):

| Tool | What it answers |
|---|---|
| `platform_overview` | Stores, people, orders and revenue today / 7 d / 30 d, pending bookings, open tickets |
| `list_stores`, `get_store` | Real stores with type, area, open state, fees, hours, rating, 30-day figures |
| `get_menu`, `search_dishes` | The live Mongo catalogue: categories, items, prices, availability |
| `list_orders`, `get_order` | Orders in a window; one order with lines, timeline, review |
| `sales_report` | Revenue/orders by day, week, month, hour or weekday + splits by status, payment, type, store |
| `top_items` | Best sellers by quantity and revenue |
| `customers` | Customers ranked by spend (phones masked unless `includeContact`) |
| `reservations`, `inventory`, `staff` | Bookings, raw materials (low-stock flag), team |
| `assistant_chats`, `visitors` | Mongo botChats and visits analytics |
| `schema` | Every SQL table/column and Mongo collection with counts and field names |

Read-only, raw (for anything the typed tools miss):

* `sql_query` — one `SELECT`/`WITH` statement, read-uncommitted transaction that is rolled
  back, row cap, 20 s timeout. Refuses DML/DDL, `EXEC`, multiple statements, comments and
  confidential columns/tables (password hashes, saved cards, push subscriptions, national ids).
* `mongo_find`, `mongo_aggregate` — filter/projection/sort or a pipeline on one collection;
  `$out`/`$merge`/server-side JS refused; `loginCodes` and Instagram token collections hidden.

Writes (off by default): `set_store_open`, `set_item_availability`.

Resources: `orderorange://schema`, `orderorange://stores`, `orderorange://overview`.
Prompts: `daily_report`, `menu_review`, `customer_insights`.

## Connecting clients

**Grok (xAI Responses API)** — remote MCP tool:

```json
{ "model": "grok-4.7",
  "input": [{ "role": "user", "content": "Which store sold the most last week?" }],
  "tools": [{ "type": "mcp", "server_url": "https://orderorange.com:8600/mcp",
              "server_label": "orderorange", "authorization": "<Mcp:Token>" }] }
```

The admin app's **Data analyst** page (`/analyst`) does exactly this through
`POST api/assistant/analyst` (administrators only) and shows every tool call behind an answer.

The partner app's assistant (the spark in the app bar) does the same for one store through
`POST api/assistant/partner` (any signed-in store login; the store comes from the JWT's
`restaurantId` claim): a store-scoped token is minted per question, reasoning effort
`Grok:PartnerReasoning` (default low), and the model may run the two store actions
(`set_store_open`, `set_item_availability`) when `Mcp:PartnerWrites` is true (default).
Floor commands (tables, bills, bookings, ringing an order) never leave the browser engine.

**Claude Code / Claude Desktop** (`.mcp.json` or `claude mcp add`):

```json
{ "mcpServers": { "orderorange": { "type": "http", "url": "https://orderorange.com:8600/mcp",
  "headers": { "Authorization": "Bearer <Mcp:Token>" } } } }
```

**curl** (REST mirror):

```bash
curl -s -X POST https://orderorange.com:8600/api/mcp/tools/sales_report \
  -H "Authorization: Bearer <Mcp:Token>" -H "Content-Type: application/json" \
  -d '{"groupBy":"week","from":"2026-09-01"}'
```

## Code

`OrderOrange.ApiServer/Mcp/` — `McpEndpoint.cs` (transport, JSON-RPC, auth, REST mirror,
OpenAPI), `McpToolbox.cs` (the tools), `McpTool.cs` (contract + argument helpers). Add a tool
by appending one `new McpTool(...)` to `McpToolbox.Build()`; it appears in MCP, REST and
OpenAPI at once. Every call is logged as `MCP tool {name} ok|refused in N ms`.

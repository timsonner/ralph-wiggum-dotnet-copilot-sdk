# RalphAgent

RalphAgent is a .NET console program that runs a short autonomous loop. Each pass starts a new GitHub Copilot session, fills the system message from `skills.md`, `goal.md`, and `memory.json`, and lets the model call tools. The tools talk to the Moltbook HTTP API. Startup also opens a Model Context Protocol (MCP) client that expects a Docker image named `kali-mcp`. The loop stops when an assistant message contains the text `SUCCESS`, or after 30 iterations (`MaxIterations` in `RalphDemo/RalphAgent/Program.cs`).

The checked-in goal is to register on Moltbook, create one introductory post, and return that post's URL or id. `goal.md` also lists a profile check. The program has no profile tool.

## Prerequisites

- .NET SDK 10.0. `RalphAgent.csproj` targets `net10.0`.
- GitHub Copilot CLI, installed and signed in. `copilot_sdk_learning_path.md` says `CopilotClient` uses the local Copilot CLI. `Program.cs` constructs `CopilotClient` and calls `StartAsync` with no endpoint argument. The session model string is `claude-sonnet-4.5`.
- Docker, with an image named `kali-mcp`. Before the Copilot loop, `Program.cs` always starts an MCP stdio client named `KaliMCP`. The client command runs `docker exec` against a container from that image, or `docker run` of that image when no such container is running. If that client fails, the process stops before the loop.

## Install, build, run, and test

From the repository root:

```bash
cd RalphDemo/RalphAgent
dotnet restore
dotnet build
dotnet run
```

`dotnet restore` and `dotnet build` are the install and build steps. There is no separate install script. `dotnet run` builds if needed and starts the agent.

There is no test project and no test script. This command builds the console app and does not execute tests:

```bash
cd RalphDemo/RalphAgent
dotnet test
```

NuGet restore can print `NU1902` and `NU1903` warnings for transitive packages `MessagePack` 2.5.198 and `Nerdbank.MessagePack` 1.0.2. Those warnings do not fail the build.

`dotnet run` needs the Copilot CLI and Docker described above. It loads `memory.json` and can call the Moltbook API and the MCP tools.

## Usage

The program resolves `memory.json`, `skills.md`, and `goal.md` three directories above the build output (`AppContext.BaseDirectory` plus `../../../`). With `dotnet run` from `RalphDemo/RalphAgent`, that path is the project directory.

Edit those files, then start the app:

```bash
cd RalphDemo/RalphAgent
dotnet run
```

- `goal.md` is copied into the system message under `GOAL`. The checked-in file asks the agent to register, confirm identity, create a post, and return the post URL or id.
- `skills.md` is copied into the system message under `CONTEXT`. The checked-in file names the Moltbook actions and says posts are limited to one per 30 minutes. The C# code does not enforce that limit.
- `memory.json` is loaded at startup. The `AgentMemory` fields are `ApiKey`, `AgentName`, `ClaimUrl`, and `LastAction`. The process rewrites the file when `save_api_key` runs, when registration stores a key, and after `create_post` or `comment`. `.gitignore` lists `RalphDemo/RalphAgent/memory.json` and `RalphDemo/RalphAgent/goal.md`. Both files are still tracked.

Each iteration prints `--- [Ralph] Iteration N ---`, opens a new Copilot session, and sends `Iteration N. What is your next move?`. If the assistant text contains `SUCCESS`, the process prints `[Ralph] Mission Accomplished.` and exits the loop. If the cap is hit first, it prints `[Ralph] Max iterations reached.`

The HTTP client base address is `https://www.moltbook.com/api/v1/`. When `ApiKey` is set, requests send `Authorization: Bearer` with that value.

Tools defined in `Program.cs`:

| Tool | Behavior |
| --- | --- |
| `save_api_key` | Stores the given key in `memory.json` and sets the bearer header. |
| `register_agent` | `POST agents/register` with `name` and `description`. Reads `agent.api_key` and `agent.claim_url` when present, otherwise `token`, `api_key`, or `key` on the JSON root. Saves the key and agent name. |
| `get_feed` | `GET posts?sort=` plus the `sort` argument. The tool description says `recent` or `popular`. |
| `create_post` | `POST posts` with `title`, `content`, and `submolt`. Sets `LastAction`. |
| `search` | `GET search?q=` with the query escaped. |
| `comment` | `POST posts/{postId}/comments` with `content`. Sends `parent_id` when `parentId` is set. Sets `LastAction`. |
| `read_memory` | Returns the current memory object as JSON. |

After the MCP client lists tools, the program registers a matching local tool only for these names:

| MCP tool | Behavior in this program |
| --- | --- |
| `kali-exec` | Forwards `command`, `image`, and `containerName` to the MCP server. |
| `kali-container-status` | Forwards `containerName`. |
| `kali-container-restart` | Forwards `image` and `containerName`. |
| `kali-container-stop` | Forwards `containerName` and `removeContainer`. |

Any other MCP tool name is printed and skipped.

Packages in `RalphAgent.csproj`:

- `GitHub.Copilot.SDK` 0.1.19
- `Microsoft.Extensions.AI` 10.2.0
- `ModelContextProtocol` 0.7.0-preview.1

## Layout

```
README.md
copilot_sdk_learning_path.md    notes on CopilotClient, sessions, and tools
mcp_csharp_sdk_learning_path.md notes on the C# Model Context Protocol SDK
ralph_pattern_csharp.md         notes on the iteration loop
RalphDemo/RalphAgent/Program.cs
RalphDemo/RalphAgent/RalphAgent.csproj
RalphDemo/RalphAgent/skills.md
RalphDemo/RalphAgent/goal.md
RalphDemo/RalphAgent/memory.json
```

There is no solution file.

## License

This repository has no `LICENSE` file.

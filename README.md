# RalphDemo - Copilot SDK Pattern

This repository provides a comprehensive showcase of how to use the **GitHub Copilot SDK for .NET** in conjunction with the **Ralph Wiggum Pattern** to build autonomous agents. Specifically, it implements an agent capable of interacting with the **Moltbook** social media platform, demonstrating how to bridge LLM reasoning with external APIs and persistent state.

## Project Structure

- **RalphAgent**: The main console application acting as the AI agent.
- **RalphAgent/Program.cs**: Contains the agent loop, Moltbook API client, and tool definitions.
- **RalphAgent/goal.md**: Defines the high-level objective for the agent (e.g., "Become a popular influencer").
- **RalphAgent/skills.md**: Lists the capabilities or persona traits the agent should embody.
- **RalphAgent/memory.json**: A persistent JSON store where the agent saves its state (API keys, identity, last actions).

## Prerequisites

- .NET 10.0 SDK
- GitHub Copilot CLI installed and authenticated
- Access to GitHub Copilot

## Getting Started

1.  **Navigate to the Agent directory:**
    ```bash
    cd RalphDemo/RalphAgent
    ```

2.  **Configure the Agent:**
    - Edit `goal.md` to set what you want the agent to achieve on Moltbook.
    - Edit `skills.md` to give the agent specific behavior guidelines.

3.  **Run the Agent:**
    ```bash
    dotnet run
    ```

    The agent will:
    -   Load its configuration and memory.
    -   Connect to the Moltbook API (`https://www.moltbook.com/api/v1/`).
    -   **Register** itself if it hasn't already (saving credentials to `memory.json`).
    -   **Observe** the feed or search for content.
    -   **Post** content based on its observations and goals.
    -   Run for a maximum of 10 iterations per session.

## Configuration

The agent's core logic is in `RalphDemo/RalphAgent/Program.cs`. Key configurable elements include:
-   **MaxIterations**: Controls how long the agent runs in one session.
-   **System Message**: Dynamically built from `skills.md`, `goal.md`, and `memory.json`.
-   **Tools**: The agent has access to Moltbook-specific tools:
    -   `register_agent`: Creates a new account.
    -   `get_feed`: Reads recent or popular posts.
    -   `create_post`: Publishes new content.
    -   `search`: Finds relevant posts.

## References

-   **Copilot SDK Learning Path**: [copilot_sdk_learning_path.md](copilot_sdk_learning_path.md)
-   **Ralph Pattern Description**: [ralph_pattern_csharp.md](ralph_pattern_csharp.md)

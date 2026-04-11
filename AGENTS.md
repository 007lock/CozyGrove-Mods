# Cozy Grove Mods - All agent Context Guide

## Memory Integration

**IMPORTANT:** Before working on this project, you MUST query the Memory system to retrieve relevant context about this codebase.

### Always Do This First

1. Use `memory_search` to find relevant documentation and code patterns (e.g., query "AutoHarvest", "throwing", "Avatar")
2. Check `memory_list` to browse all available memories by tag
3. Review any retrieved memories to understand existing patterns and decisions

### Memory Structure

Memories are tagged and categorized for easy retrieval. Common tags include:
- `cozy-grove-mods` - Cozy Grove mod development patterns
- `mod-development` - General mod development utilities
- `game-interfaces` - Game API classes from Assembly-CSharp
- `auto-harvest` - Auto-harvest mod specifics
- `auto-fishing` - Auto-fishing mod specifics
- `auto-net` - Auto-netting mod specifics
- `simon-homestead` - Simon Homestead Go application patterns
- `simon-homestead-code` - Code structure and APIs

Memory types used:
- `code-pattern` - Reusable code patterns and implementations
- `bug-fix` - Solutions to specific bugs and issues
- `reference` - Class references and API documentation
- `architecture` - System architecture overviews
- `api-documentation` - REST API endpoint documentation
- `configuration` - YAML and workflow configurations
- `changelog` - Version changes and upgrades

### When to Update Context

- After completing a significant feature or fix
- When discovering new game interface classes
- When creating a new mod project
- When learning something non-obvious about the codebase

### How to Update Context

Use `memory_store` to add new knowledge:
```json
{
  "content": "New discovery here...",
  "metadata": {
    "tags": "cozy-grove-mods,mod-development",
    "type": "code-pattern"
  }
}
```

The Memory system uses semantic search, so store concise, factual information with appropriate tags for future retrieval.

## Project Structure

This is a modding framework for the game Cozy Grove. Each mod is typically a separate project under `projects/`.

## Game Interface Reference

See `resources/dump.cs` for the decompiled game assembly. Key interfaces are documented in Memory with tags `cozy-grove-mods` and `game-interfaces`.
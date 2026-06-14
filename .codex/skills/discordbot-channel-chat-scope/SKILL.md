---
name: discordbot-channel-chat-scope
description: Use when changing or reviewing DiscordBot chat persistence, chat history lookup, search, context loading, attachments tied to chat messages, Discord tool calls that read channel chat, or any feature that stores or retrieves Discord channel conversation data.
---

# DiscordBot Channel Chat Scope

## Core Rule

- Keep channel chat operations scoped to the Discord channel where the chat is happening.
- Do not let chat history, search, context lookup, attachment lookup, or message inspection read conversation data from another channel unless a future feature explicitly defines and authorizes that cross-channel behavior.
- Treat channel-scoped chat data as a privacy and authorization boundary, not just a query convenience.

## Persistence Requirements

- Store the Discord channel identifier whenever saving chat-related records that may later be queried, searched, linked, inspected, or used as context.
- Preserve channel identity alongside Discord message identifiers, guild identifiers when available, author identifiers, attachments, images, extracted text, and any derived chat metadata.
- Ensure repository methods that retrieve chat-related data require or derive the current channel id and filter by it.
- Avoid APIs that accept only a message id or database id for chat lookup when that lookup could cross channels; require channel context as part of the lookup.

## Discord Tooling Requirements

- Tools exposed to the bot for chat history, search, context loading, attachment lookup, image lookup, or message inspection must operate within the current Discord channel by default.
- If a user supplies a Discord message URL or message id, validate that the target message belongs to the current channel before returning stored chat data.
- If the channel cannot be verified, fail closed or ask for clarification rather than searching globally.

## Review Checklist

- Verify every new chat-related table, record, DTO, repository method, and query preserves and filters by channel id.
- Verify chat lookup tools cannot use message ids, attachment ids, image ids, or internal database ids to retrieve another channel's content.
- Verify tests cover the channel isolation behavior when the logic is practical to test.
- Treat missing channel scoping in DiscordBot chat persistence or retrieval as a merge-readiness blocker.

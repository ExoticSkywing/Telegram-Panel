# Telegram Panel

[English](README.md) | [中文](README.zh-CN.md)

A **WTelegramClient**-based multi-account Telegram management panel built with a **.NET 8** backend and a **Vue 3** management UI.

<p align="center">
  <a href="https://t.me/zhanzhangck"><img src="https://img.shields.io/badge/Telegram-站长仓库-blue?logo=telegram" alt="Telegram 站长仓库"></a>
  <a href="https://t.me/vpsbbq"><img src="https://img.shields.io/badge/Telegram-NexHub_AI社区-blue?logo=telegram" alt="Telegram NexHub AI社区"></a>
</p>

## Introduction

Telegram Panel is a unified web panel for managing and operating multiple Telegram accounts, focusing on account lifecycle management, batch operations, channel/group management, automated tasks, and module extension capabilities.

## Features

- 📥 **Multi-account Import / Login**: Supports Telethon / TData archive import/export, phone verification login, 2FA password handling
- 👥 **Batch Operations**: Batch join/subscribe/leave groups, auto-send messages in private groups, batch invite members/bots, batch set admins, export links, etc.
- 📱 **One-click Device Logout**: Keep current panel session, clean up other online devices
- 🧹 **Dead Account Detection & Cleanup**: Batch process banned, restricted, frozen, inactive, and session-expired accounts
- 🔐 **2FA Management**: Support single/batch password change, bind/rebind recovery email
- 👤 **Account Visibility Enhancement**: View joined channels/groups with estimated registration time
- 🧩 **Module Extension**: Installable task / API modules, Vue management APIs, and legacy Razor module page compatibility (see docs/developer/modules.md)

## Frontend Architecture

The main management UI is now a Vue SPA served under `/ui`. The .NET backend still hosts the API, background tasks, module loader, legacy Razor module pages, and compatibility routes.

For module authors:

- New modules should expose management data through `/api/panel/extensions/{module-slug}` and let the Vue UI consume those APIs.
- Existing Razor module pages registered through `IModuleUiProvider.GetPages` still work through `/ext/{moduleId}/{pageKey}` and are loaded by the Vue shell when no native Vue page exists.
- Public customer-facing links should be implemented as explicit module endpoints with their own token, expiry, cache, and rate-limit rules, not as admin module pages.

## Quick Start

### Docker Deployment (Recommended)

Requirements: Docker (Windows: Docker Desktop + WSL2; Linux: Docker Engine)

```````bash
git clone https://github.com/moeacgx/Telegram-Panel
cd Telegram-Panel
cp .env.example .env
docker compose pull
docker compose up -d
```````

Access: http://localhost:5000

If port `5000` conflicts with another service, keep the container port as `5000` and map a higher host port instead:

```````yaml
ports:
  - "18080:5000"
```````

Then access: http://localhost:18080

Default credentials: admin / admin123

### Local Development

```````bash
dotnet run --project src/TelegramPanel.Web
```````

Access: http://localhost:5000

## Screenshots

See screenshot/ folder.

## Star History

https://star-history.com/#moeacgx/Telegram-Panel&Date

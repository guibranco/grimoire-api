---
title: API Reference
nav_order: 4
has_children: true
permalink: /api-reference/
---

# 📡 API Reference

Complete reference for the Grimoire REST API.

---

## In this section

| Page | What you'll learn |
| :--- | :---------------- |
| [Authentication]({{ site.baseurl }}/api-reference/authentication/) | How to authenticate against both API planes |
| [Management API]({{ site.baseurl }}/api-reference/management-api/) | Full CRUD reference — applications, environments, secrets, configs |
| [Consumer API]({{ site.baseurl }}/api-reference/consumer-api/) | Read-only reference — getting secrets and configuration at runtime |

---

## Base URL

By default the API runs on port `8080`:

```
http://localhost:8080
```

---

## Response format

All responses use **camelCase JSON**. Errors follow the [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) format:

```json
{
  "type":   "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title":  "Unauthorized",
  "status": 401
}
```

---

## HTTP status codes

| Code | Meaning |
| :--- | :------ |
| `200 OK` | Successful read or update |
| `201 Created` | Resource created |
| `204 No Content` | Successful delete |
| `400 Bad Request` | Validation error (FluentValidation) |
| `401 Unauthorized` | Missing or invalid credentials |
| `404 Not Found` | Resource does not exist |
| `409 Conflict` | Duplicate resource (slug, key, etc.) |
| `500 Internal Server Error` | Unexpected server error |

---

## Interactive docs

The Swagger UI is available in `Development` environment:

- **Management API:** `http://localhost:8080/swagger` → select "Management API"
- **Consumer API:** `http://localhost:8080/swagger` → select "Consumer API"

#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { z } from "zod";

const apiBaseUrl = process.env.AICAN_API_BASE_URL ?? "http://127.0.0.1:5000";

const searchSchema = z.object({
  userEmail: z.string().email(),
  displayName: z.string().min(1),
  department: z.string().min(1),
  query: z.string().min(1)
});

const accessSchema = z.object({
  userEmail: z.string().email(),
  displayName: z.string().min(1),
  department: z.string().min(1),
  documentId: z.string().uuid(),
  reason: z.string().min(1)
});

const reclassifySchema = z.object({
  userEmail: z.string().email(),
  displayName: z.string().min(1),
  department: z.string().min(1),
  documentId: z.string().uuid(),
  proposedCategory: z.string().min(1),
  reason: z.string().min(1)
});

const profileSchema = z.object({
  userEmail: z.string().email(),
  displayName: z.string().min(1),
  department: z.string().min(1)
});

async function createSession(args) {
  const response = await fetch(`${apiBaseUrl}/session/exchange`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: args.userEmail,
      displayName: args.displayName,
      botName: "AiCan",
      department: args.department,
      m365AccessToken: null
    })
  });

  if (!response.ok) {
    throw new Error(`session exchange failed with ${response.status}`);
  }

  return response.json();
}

async function callAiCan(path, sessionToken, method = "GET", body = null) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      "content-type": "application/json",
      "x-aican-session": sessionToken
    },
    body: body ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${method} ${path} failed with ${response.status}: ${text}`);
  }

  return response.json();
}

const server = new Server(
  {
    name: "aican-openclaw-tools",
    version: "0.1.0"
  },
  {
    capabilities: {
      tools: {}
    }
  }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: [
    {
      name: "aican_assistant_profile",
      description: "Fetch the AiCan assistant profile for a given employee context.",
      inputSchema: {
        type: "object",
        properties: {
          userEmail: { type: "string" },
          displayName: { type: "string" },
          department: { type: "string" }
        },
        required: ["userEmail", "displayName", "department"]
      }
    },
    {
      name: "aican_search_documents",
      description: "Search AiCan documents using the employee's access scope.",
      inputSchema: {
        type: "object",
        properties: {
          userEmail: { type: "string" },
          displayName: { type: "string" },
          department: { type: "string" },
          query: { type: "string" }
        },
        required: ["userEmail", "displayName", "department", "query"]
      }
    },
    {
      name: "aican_request_access",
      description: "Submit an AiCan document access request.",
      inputSchema: {
        type: "object",
        properties: {
          userEmail: { type: "string" },
          displayName: { type: "string" },
          department: { type: "string" },
          documentId: { type: "string" },
          reason: { type: "string" }
        },
        required: ["userEmail", "displayName", "department", "documentId", "reason"]
      }
    },
    {
      name: "aican_suggest_reclassification",
      description: "Submit an AiCan document reclassification suggestion.",
      inputSchema: {
        type: "object",
        properties: {
          userEmail: { type: "string" },
          displayName: { type: "string" },
          department: { type: "string" },
          documentId: { type: "string" },
          proposedCategory: { type: "string" },
          reason: { type: "string" }
        },
        required: ["userEmail", "displayName", "department", "documentId", "proposedCategory", "reason"]
      }
    }
  ]
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const name = request.params.name;
  const args = request.params.arguments ?? {};

  if (name === "aican_assistant_profile") {
    const parsed = profileSchema.parse(args);
    const session = await createSession(parsed);
    const profile = await callAiCan("/assistant/profile", session.sessionToken);
    return { content: [{ type: "text", text: JSON.stringify(profile, null, 2) }] };
  }

  if (name === "aican_search_documents") {
    const parsed = searchSchema.parse(args);
    const session = await createSession(parsed);
    const results = await callAiCan("/documents/search", session.sessionToken, "POST", { query: parsed.query });
    return { content: [{ type: "text", text: JSON.stringify(results, null, 2) }] };
  }

  if (name === "aican_request_access") {
    const parsed = accessSchema.parse(args);
    const session = await createSession(parsed);
    const result = await callAiCan("/actions/access-request", session.sessionToken, "POST", {
      documentId: parsed.documentId,
      reason: parsed.reason
    });
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
  }

  if (name === "aican_suggest_reclassification") {
    const parsed = reclassifySchema.parse(args);
    const session = await createSession(parsed);
    const result = await callAiCan("/actions/reclassification-suggest", session.sessionToken, "POST", {
      documentId: parsed.documentId,
      proposedCategory: parsed.proposedCategory,
      reason: parsed.reason
    });
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
  }

  throw new Error(`Unknown tool: ${name}`);
});

const transport = new StdioServerTransport();
await server.connect(transport);

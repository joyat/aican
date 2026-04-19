# Sungas Technologies Ltd. — Product Portfolio

**Document type:** Product Knowledge  
**Audience:** All employees (Sales, CS, Engineering, and anyone with customer-facing responsibilities)  
**Version:** 4.0 (FY2025 edition)  
**Owner:** Product team — Mei Lin Zhao (Head of Product, SunAI), Marcus Tan (VP Engineering)

---

## Overview

Sungas builds three interconnected enterprise platforms. Customers may purchase each independently, but the full value of the stack is realised when SunCloud (data foundation), SunAI (intelligence), and SunShield (security and compliance) work together.

**Current ARR contribution:**
- SunCloud Platform: 48%
- SunAI Analytics: 37%
- SunShield Security: 15% (growing fastest)

---

## 1. SunCloud Platform

### What It Does
SunCloud is an enterprise data management and workflow automation platform. It helps organisations move data securely across cloud environments, automate complex multi-step business processes, and connect disparate enterprise systems without custom development.

### Core Modules

**DataVault**
Secure multi-cloud data storage and management. Supports AWS S3, Azure Blob, and GCP Cloud Storage backends. Key capabilities:
- End-to-end encryption at rest and in transit (AES-256, TLS 1.3)
- Data residency controls — organisations specify which cloud region stores which data
- Access audit log — every read, write, and share event is logged and queryable
- Immutable storage mode for regulatory record retention
- MAS TRM, GDPR, and ISO 27001 alignment built into the data model

**WorkflowEngine**
Low-code process automation. Enables non-technical users to build, test, and deploy multi-step business workflows via a drag-and-drop canvas. Supports:
- 500+ pre-built workflow templates (onboarding, document approval, reconciliation, etc.)
- Conditional logic, parallel branches, and human approval steps
- SLA monitoring and automated escalation
- Full audit trail per workflow execution
- Integration with IntegrationHub connectors

**IntegrationHub**
Pre-built connectors to 500+ enterprise systems including Salesforce, SAP, Oracle ERP, Workday, ServiceNow, Microsoft 365, Google Workspace, and major banking systems. Custom connectors available via REST, SOAP, and GraphQL adapters. No-code connector builder released in Q3 2024.

### Target Customers
Mid-market to enterprise organisations in BFSI, healthcare, and government. Typical deal size: SGD 120K–SGD 800K ARR. Implementation time: 6–16 weeks depending on scope.

### Key Differentiators vs. Competition
- Built-for-compliance architecture: most competitors added compliance as a layer; Sungas built it into the data model from the start
- Asia-Pacific data residency expertise: Sungas has deeper MAS TRM and Australian Privacy Act experience than most US-headquartered competitors
- Single platform: DataVault + WorkflowEngine + IntegrationHub are one unified system — no stitching three products together

---

## 2. SunAI Analytics

### What It Does
SunAI is a decision-intelligence platform that turns data stored in SunCloud (or other sources) into forecasts, alerts, and executive dashboards. It is built on Sungas's proprietary ML pipeline developed by the Bangalore R&D team, trained initially on BFSI data patterns.

### Core Modules

**InsightDash**
Executive and operational dashboards. Connect to any data source (SunCloud, SQL databases, Salesforce, third-party APIs). Supports real-time and scheduled refresh. Role-based access controls ensure executives see company-wide views while managers see their scope only. Mobile-optimised for iOS and Android.

**ForecastAI**
ML-based forecasting for business and financial metrics. Core use cases:
- Revenue forecasting (subscription churn, renewal probability, upsell likelihood)
- Cash flow forecasting (30/60/90-day projections)
- Demand forecasting (transaction volumes, resource utilisation)
- Accuracy benchmarks: typically 12–18% improvement over customer's existing Excel/BI tool models in the first year

**RiskRadar**
Real-time anomaly detection and risk monitoring. Originally designed for financial fraud detection, now used across sectors for:
- Transaction monitoring (unusual patterns, velocity checks)
- Operational risk signals (system health, SLA breach prediction)
- Compliance monitoring (policy breach detection, automated alerting)
- Supports custom risk models trained on customer data (Bring Your Own Model capability, released Q2 2024)

### Target Customers
Existing SunCloud customers (cross-sell/upsell path) and greenfield enterprise customers with large structured data assets. Typical deal size: SGD 80K–SGD 400K ARR as add-on. Standalone SunAI deals starting at SGD 60K ARR.

### Key Differentiators
- Pre-trained models optimised for APAC financial data patterns (significant advantage in Singapore, Australia, India)
- Deep SunCloud integration means no ETL work for existing customers — analytics works on data already in the vault
- Explainable AI output: every ForecastAI prediction includes a plain-English explanation of contributing factors (regulatory requirement for several clients)

---

## 3. SunShield Security

### What It Does
SunShield is an enterprise data security and compliance platform built on the CipherNest DLP technology acquired in 2022. It classifies data, enforces access policies, maps regulatory requirements, and provides security operations teams with visibility across the data estate.

### Core Modules

**DataGuard (DLP)**
Data-loss prevention engine. Monitors data movement across email, messaging platforms, cloud storage, and endpoints. Key capabilities:
- Auto-classification of sensitive data (PII, PCI-DSS, PHI, trade secrets) using ML classifiers
- Policy-based blocking: prevent download, forward, or share of classified data
- Incident alerts to SIEM platforms (Splunk, Microsoft Sentinel, QRadar integrations)
- Works across SunCloud and non-SunCloud data sources

**ComplianceMap**
Regulatory mapping and gap analysis tool. Built-in regulatory frameworks:
- MAS TRM (Technology Risk Management) — Singapore financial institutions
- GDPR — EU/UK personal data
- PDPA — Singapore Personal Data Protection Act
- Australian Privacy Act
- ISO 27001 Information Security Management
- SOC 2 Type II
Generates compliance readiness reports, tracks control gaps, and creates evidence packs for audits.

**ZeroTrust Policy Engine**
Identity and access policy enforcement. Integrates with Azure AD, Okta, and Ping Identity. Supports:
- Attribute-based access control (ABAC) on data objects
- Continuous verification (not just at login) — real-time session risk scoring
- Privileged access management (PAM) for admin accounts
- Just-in-time access requests with auto-expiry

### Target Customers
Any enterprise handling regulated or sensitive data. Strong PMF in financial services (MAS TRM requirements drive urgency), healthcare (PDPA/PHI), and government (data security mandates). Typical deal size: SGD 60K–SGD 350K ARR. Often sold as part of a full SunCloud + SunShield bundle.

### Key Differentiators
- CipherNest DLP heritage: 8 years of DLP development before acquisition — not a new entrant
- APAC regulatory depth: pre-built MAS TRM and PDPA mappings save customers 3–6 months of compliance work
- Unified platform: SunShield + SunCloud means classification, storage policy, and access control are managed in one place

---

## 4. Pricing Overview (Internal Reference — Confidential)

Pricing is discussed only with Sales, Finance, and executive stakeholders. Do not share specific pricing information with non-authorised colleagues or external parties.

**Indicative bands (ACV — Annual Contract Value):**

| Product | SMB (under 1,000 employees) | Mid-market (1,000–10,000) | Enterprise (10,000+) |
|---------|---------------------------|--------------------------|---------------------|
| SunCloud Platform | SGD 40K–120K | SGD 120K–400K | SGD 400K–800K |
| SunAI Analytics (add-on) | SGD 20K–60K | SGD 60K–200K | SGD 200K–400K |
| SunShield Security | SGD 25K–80K | SGD 80K–250K | SGD 250K–500K |

All pricing is subject to commercial approval. Non-standard discounts above 20% require VP Sales and CFO sign-off.

---

## 5. Competitive Landscape

| Competitor | Primary product overlap | Sungas advantage |
|-----------|------------------------|-----------------|
| Microsoft Purview | Data compliance, DLP | Better APAC regulatory coverage; lower implementation cost for non-MSFT shops |
| Salesforce Data Cloud | Data integration, analytics | Sungas works across all CRMs; not locked to Salesforce ecosystem |
| Palantir (Foundry) | Data analytics, AI | Sungas is more accessible (lower cost) for mid-market; faster deployment |
| IBM OpenPages | Compliance management | Sungas is cloud-native; OpenPages is legacy on-prem |
| Snowflake (data platform) | Data warehousing | Snowflake is a data warehouse; SunCloud is a data management platform — different buyer |

---

## 6. Product Roadmap Highlights (H1 FY2025 — Internal Only, Do Not Share Externally)

- **SunCloud**: Multi-tenant workspace features for enterprise subsidiaries (GA Q2 2025)
- **SunAI**: Natural language query interface ("SunAI Chat") for InsightDash — beta Q2, GA Q3 2025
- **SunShield**: AI-powered insider threat detection module — GA Q3 2025
- **Platform**: Singapore Government Commercial Cloud (GCC+) certification — expected Q4 2025

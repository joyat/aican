from __future__ import annotations

from pathlib import Path
from typing import List

from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI(title="AiCan Worker", version="0.1.0")


class ExtractRequest(BaseModel):
    file_path: str


class ExtractResponse(BaseModel):
    file_path: str
    extracted_text: str


class ClassifyRequest(BaseModel):
    file_name: str
    extracted_text: str


class ClassifyResponse(BaseModel):
    category: str
    confidence: float
    customer_name: str


class EmbedRequest(BaseModel):
    text: str


class EmbedResponse(BaseModel):
    embedding: List[float]


def classify(file_name: str, extracted_text: str) -> ClassifyResponse:
    corpus = f"{file_name} {extracted_text}".lower()
    if "invoice" in corpus:
        return ClassifyResponse(category="invoice", confidence=0.92, customer_name="unassigned")
    if "hr" in corpus or "employee" in corpus:
        return ClassifyResponse(category="hr", confidence=0.88, customer_name="internal")
    return ClassifyResponse(category="general", confidence=0.71, customer_name="unassigned")


@app.get("/healthz")
async def healthz() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/extract", response_model=ExtractResponse)
async def extract(request: ExtractRequest) -> ExtractResponse:
    path = Path(request.file_path)
    if path.exists() and path.suffix.lower() in {".txt", ".md"}:
        text = path.read_text(encoding="utf-8", errors="ignore")
    else:
        text = f"placeholder extracted text from {path.name}"
    return ExtractResponse(file_path=request.file_path, extracted_text=text)


@app.post("/classify", response_model=ClassifyResponse)
async def classify_endpoint(request: ClassifyRequest) -> ClassifyResponse:
    return classify(request.file_name, request.extracted_text)


@app.post("/embed", response_model=EmbedResponse)
async def embed(request: EmbedRequest) -> EmbedResponse:
    values = [0.0] * 8
    for index, character in enumerate(request.text[:128]):
        values[index % len(values)] += ord(character) / 255.0
    return EmbedResponse(embedding=values)

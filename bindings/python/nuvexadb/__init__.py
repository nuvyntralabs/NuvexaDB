"""Python SDK for NuvexaDB over the Native AOT C ABI."""

__version__ = "1.0.0"

from .database import (
    NuvexaDatabase,
    NuvexaDocument,
    NuvexaEncryptionException,
    NuvexaException,
    NuvexaIntegrityException,
)

__all__ = [
    "NuvexaDatabase",
    "NuvexaDocument",
    "NuvexaEncryptionException",
    "NuvexaException",
    "NuvexaIntegrityException",
]

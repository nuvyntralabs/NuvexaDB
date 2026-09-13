package nuventra.nuvexadb

open class NuvexaException @JvmOverloads constructor(
    message: String,
    cause: Throwable? = null
) : RuntimeException(message, cause)

class NuvexaEncryptionException @JvmOverloads constructor(
    message: String,
    cause: Throwable? = null
) : NuvexaException(message, cause)

class NuvexaIntegrityException @JvmOverloads constructor(
    message: String,
    cause: Throwable? = null
) : NuvexaException(message, cause)

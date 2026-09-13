package nuventra.nuvexadb

import org.json.JSONObject

class NuvexaDocument(val json: JSONObject) {
    val id: String
        get() = json.optString("_id", "")

    @JvmOverloads
    fun field(name: String, default: String? = null): String? =
        if (json.has(name) && !json.isNull(name)) json.get(name).toString() else default

    override fun toString(): String = json.toString()

    companion object {
        @JvmStatic
        fun parse(json: String): NuvexaDocument = NuvexaDocument(JSONObject(json))
    }
}

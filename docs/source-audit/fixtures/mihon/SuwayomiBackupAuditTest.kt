package eu.kanade.tachiyomi.data.backup

import eu.kanade.tachiyomi.data.backup.models.Backup
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.protobuf.ProtoBuf
import org.junit.jupiter.api.Test
import java.io.File
import java.util.zip.GZIPInputStream

@OptIn(ExperimentalSerializationApi::class)
class SuwayomiBackupAuditTest {
    @Test
    fun `Mihon decodes a Suwayomi export without losing source or chapter identity`() {
        val fixture = File(checkNotNull(System.getenv("MANGA_AUDIT_BACKUP")))
        val bytes = fixture.inputStream().use { file -> GZIPInputStream(file).use { it.readBytes() } }
        val backup = ProtoBuf.decodeFromByteArray(Backup.serializer(), bytes)
        val work = backup.backupManga.single { it.title == "Synthetic Audit Work" }
        check(work.source == 7200000000000000123L) { "Unexpected source: ${work.source}" }
        val chapter = work.chapters.single { it.name == "Chapter 12.5" }
        check(chapter.chapterNumber == 12.5f) { "Unexpected chapter number: ${chapter.chapterNumber}" }
    }
}

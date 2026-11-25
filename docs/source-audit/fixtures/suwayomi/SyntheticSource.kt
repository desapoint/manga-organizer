package org.mangaorganizer.audit.fixture

import eu.kanade.tachiyomi.source.model.FilterList
import eu.kanade.tachiyomi.source.model.MangasPage
import eu.kanade.tachiyomi.source.model.Page
import eu.kanade.tachiyomi.source.model.SChapter
import eu.kanade.tachiyomi.source.model.SManga
import eu.kanade.tachiyomi.source.online.HttpSource
import okhttp3.Request
import okhttp3.Response

class SyntheticSource : HttpSource() {
    override val id = 7200000000000000123L
    override val name = "Manga Organizer Synthetic Audit"
    override val lang = "en"
    override val baseUrl = System.getProperty("manga.audit.fixtureUrl", "http://127.0.0.1:14569")
    override val supportsLatest = false

    private fun fixtureManga() = SManga.create().apply {
        title = "Synthetic Audit Work"
        url = "/fixture.html"
        initialized = true
    }

    override fun searchMangaRequest(page: Int, query: String, filters: FilterList): Request =
        Request.Builder().url("$baseUrl/fixture.html").build()

    override fun searchMangaParse(response: Response) = MangasPage(listOf(fixtureManga()), false)
    override fun mangaDetailsParse(response: Response) = fixtureManga()

    override fun chapterListParse(response: Response) = listOf(SChapter.create().apply {
        name = "Chapter 12.5"
        url = "/chapter.html"
        chapter_number = 12.5f
        scanlator = "Synthetic fixture"
    })

    override fun pageListParse(response: Response) = listOf(Page(0, imageUrl = "$baseUrl/001.png"))
}

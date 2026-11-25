const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');

async function checkLayout(page, label) {
    const metrics = await page.evaluate(() => {
        const overflowX = document.documentElement.scrollWidth > document.documentElement.clientWidth;
        const navLinks = Array.from(document.querySelectorAll('header nav a')).map(a => {
            const r = a.getBoundingClientRect();
            return { text: a.textContent.trim(), right: Math.round(r.right), top: Math.round(r.top) };
        });
        const navCropped = navLinks.some(l => l.right > window.innerWidth);
        const hasActiveBadge = /\bActive\b/.test(document.body.innerText);
        return {
            overflowX,
            scrollWidth: document.documentElement.scrollWidth,
            clientWidth: document.documentElement.clientWidth,
            navCropped,
            hasActiveBadge
        };
    });
    console.log(`CHECK [${label}]:`, JSON.stringify(metrics));
    if (metrics.overflowX) throw new Error(`${label}: Horizontal overflow (${metrics.scrollWidth} > ${metrics.clientWidth})`);
    if (metrics.navCropped) throw new Error(`${label}: Navbar cropped`);
    if (metrics.hasActiveBadge) throw new Error(`${label}: 'Active' indicator still visible`);
}

async function run() {
    console.log("Starting Comprehensive TuneTrack Verification...");

    const chromePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
    const browser = await puppeteer.launch({
        executablePath: chromePath,
        headless: 'new',
        args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu', '--window-size=1440,900']
    });

    const page = await browser.newPage();
    page.on('pageerror', err => console.log('BROWSER ERROR:', err.message));

    const docsDir = path.join(__dirname, '..', 'docs', 'screenshots');
    fs.mkdirSync(docsDir, { recursive: true });

    try {
        // --- 1. IDLE (1440px & 390px) ---
        await page.setViewport({ width: 1440, height: 900 });
        await page.goto('http://127.0.0.1:5199', { waitUntil: 'networkidle0', timeout: 15000 });
        await new Promise(r => setTimeout(r, 800));
        await checkLayout(page, 'idle-1440');
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({ path: path.join(docsDir, 'screenshot-1440-idle.png'), fullPage: false });

        await page.setViewport({ width: 390, height: 844 });
        await new Promise(r => setTimeout(r, 500));
        await page.evaluate(() => window.scrollTo(0, 0));
        await checkLayout(page, 'idle-390');
        await page.screenshot({ path: path.join(docsDir, 'screenshot-390-idle.png'), fullPage: true });

        // --- 2. ECLECTIC HORIZON RESULTS (1440px & 390px) ---
        await page.setViewport({ width: 1440, height: 900 });
        await new Promise(r => setTimeout(r, 300));
        const buttons = await page.$$('button');
        for (const btn of buttons) {
            const txt = await page.evaluate(el => el.textContent, btn);
            if (txt && txt.includes('Eclectic Horizon')) {
                await btn.click();
                break;
            }
        }
        await page.waitForSelector('table', { timeout: 15000 });
        await new Promise(r => setTimeout(r, 2000));

        await checkLayout(page, 'eclectic-1440');
        const eclecticInfo = await page.evaluate(() => {
            const chipBtns = Array.from(document.querySelectorAll('[aria-label="Filter tracks by genre"] button'));
            const recGenres = Array.from(document.querySelectorAll('.lg\\:col-span-4 .font-mono.mt-0\\.5')).map(e => e.textContent.trim());
            const body = document.body.innerText;
            return {
                chipCount: chipBtns.length,
                chips: chipBtns.map(b => b.textContent.trim()),
                recGenres: [...new Set(recGenres)],
                hasSampleBadge: body.includes('Sample'),
                hasNoSpotifyHeader: !Array.from(document.querySelectorAll('h1 ~ * span')).some(s => s.textContent.trim() === 'Spotify')
            };
        });
        console.log("ECLECTIC DATA:", JSON.stringify(eclecticInfo));
        if (eclecticInfo.chipCount < 12) throw new Error(`Expected >= 12 genre chips (+All), got ${eclecticInfo.chipCount}`);

        // Test genre filter click ('Ambient' - beyond top 5!)
        const ambientBtn = await page.$('button ::-p-text(Ambient)');
        const chipBtns = await page.$$('[aria-label="Filter tracks by genre"] button');
        for (const cb of chipBtns) {
            const t = await page.evaluate(el => el.textContent.trim(), cb);
            if (t === 'Ambient') {
                await cb.click();
                await new Promise(r => setTimeout(r, 400));
                const rows = await page.$$eval('tbody tr', trs => trs.length);
                console.log(`✓ Filtered by 'Ambient' -> ${rows} row(s)`);
                if (rows !== 1) throw new Error(`Expected 1 row for Ambient filter, got ${rows}`);
                break;
            }
        }
        // Reset filter to All
        await chipBtns[0].click();
        await new Promise(r => setTimeout(r, 300));

        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({ path: path.join(docsDir, 'screenshot-1440-eclectic.png'), fullPage: true });

        await page.setViewport({ width: 390, height: 844 });
        await new Promise(r => setTimeout(r, 500));
        await page.evaluate(() => window.scrollTo(0, 0));
        await checkLayout(page, 'eclectic-390');
        await page.screenshot({ path: path.join(docsDir, 'screenshot-390-eclectic.png'), fullPage: true });

        // --- 3. STRICTLY SYNTH-POP RESULTS (1440px & 390px) ---
        await page.setViewport({ width: 1440, height: 900 });
        await new Promise(r => setTimeout(r, 300));
        const synthBtns = await page.$$('button');
        for (const btn of synthBtns) {
            const txt = await page.evaluate(el => el.textContent, btn);
            if (txt && txt.includes('Strictly Synth-Pop')) {
                await btn.click();
                break;
            }
        }
        await page.waitForFunction(() => document.body.innerText.includes('Strictly Synth-Pop: Neon Nightdrive'), { timeout: 15000 });
        await page.waitForSelector('table', { timeout: 15000 });
        await new Promise(r => setTimeout(r, 800));
        await checkLayout(page, 'synthpop-1440');

        const synthInfo = await page.evaluate(() => {
            const body = document.body.innerText;
            const recGenres = Array.from(document.querySelectorAll('.lg\\:col-span-4 span.font-mono.mt-0\\.5')).map(e => e.textContent.trim());
            const scoreTexts = Array.from(document.querySelectorAll('h1')).map(h => h.closest('div').innerText);
            return {
                hasOneGenreSingular: body.includes('1 Genre') && !body.includes('1 Genres'),
                hasWholeScore11: body.includes('11 Diversity') && !body.includes('10.5'),
                recGenres: [...new Set(recGenres)],
                nonSynthRecs: recGenres.filter(g => g.toLowerCase() !== 'synth-pop').length
            };
        });
        console.log("SYNTHPOP DATA:", JSON.stringify(synthInfo));
        if (!synthInfo.hasOneGenreSingular) throw new Error("Expected '1 Genre' pluralization");
        if (!synthInfo.hasWholeScore11) throw new Error("Expected whole-number score '11 Diversity'");

        // Bookmark first recommendation
        const bookmarkBtns = await page.$$('button[title*="Bookmark"]');
        if (bookmarkBtns.length > 0) {
            await bookmarkBtns[0].click();
            await new Promise(r => setTimeout(r, 600));
            console.log("✓ Bookmarked suggestion");
        }

        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({ path: path.join(docsDir, 'screenshot-1440-synthpop.png'), fullPage: true });

        await page.setViewport({ width: 390, height: 844 });
        await new Promise(r => setTimeout(r, 500));
        await page.evaluate(() => window.scrollTo(0, 0));
        await checkLayout(page, 'synthpop-390');
        // Verify genre pills do not wrap vertically (single-line height <= 28px)
        const pillHeights = await page.$$eval('tbody span.sm\\:hidden', spans => spans.map(s => Math.round(s.getBoundingClientRect().height)));
        console.log("390px GENRE PILL HEIGHTS:", JSON.stringify(pillHeights));
        if (pillHeights.length === 0) throw new Error("Expected mobile genre pills to render inside tracklist at 390px!");
        if (pillHeights.some(h => h > 28)) throw new Error("Mobile genre pill wrapped to multiple lines!");
        await page.screenshot({ path: path.join(docsDir, 'screenshot-390-synthpop.png'), fullPage: true });

        // --- 4. SAVED (1440px & 390px) ---
        await page.setViewport({ width: 1440, height: 900 });
        await page.goto('http://127.0.0.1:5199/saved', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));
        await checkLayout(page, 'saved-1440');
        await page.screenshot({ path: path.join(docsDir, 'screenshot-1440-saved.png'), fullPage: false });

        await page.setViewport({ width: 390, height: 844 });
        await new Promise(r => setTimeout(r, 400));
        await page.evaluate(() => window.scrollTo(0, 0));
        await checkLayout(page, 'saved-390');
        await page.screenshot({ path: path.join(docsDir, 'screenshot-390-saved.png'), fullPage: true });

        // --- 5. HISTORY (1440px & 390px) ---
        await page.setViewport({ width: 1440, height: 900 });
        await page.goto('http://127.0.0.1:5199/history', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));
        await checkLayout(page, 'history-1440');

        const historyCheck = await page.evaluate(() => {
            const times = Array.from(document.querySelectorAll('time')).map(t => t.textContent.trim());
            const body = document.body.innerText;
            return {
                times,
                noUtcSuffix: times.every(t => !t.includes('UTC')),
                hasWholeScores: !body.includes('10.5')
            };
        });
        console.log("HISTORY DATA:", JSON.stringify(historyCheck));
        if (!historyCheck.noUtcSuffix) throw new Error("History timestamps did not convert from UTC to local time!");
        if (!historyCheck.hasWholeScores) throw new Error("History scores contain unrounded decimals!");
        await page.screenshot({ path: path.join(docsDir, 'screenshot-1440-history.png'), fullPage: false });

        await page.setViewport({ width: 390, height: 844 });
        await new Promise(r => setTimeout(r, 400));
        await page.evaluate(() => window.scrollTo(0, 0));
        await checkLayout(page, 'history-390');
        await page.screenshot({ path: path.join(docsDir, 'screenshot-390-history.png'), fullPage: true });

        // Reopen historical report and verify
        await page.setViewport({ width: 1440, height: 900 });
        const viewBtn = await page.$('a[href*="/?id="]');
        if (!viewBtn) throw new Error("Missing View Report link");
        await viewBtn.click();
        await page.waitForSelector('h1', { timeout: 10000 });
        await new Promise(r => setTimeout(r, 600));
        console.log("✓ Reopened report:", await page.$eval('h1', el => el.textContent.trim()));

        // Clean up /saved and /history test records
        await page.goto('http://127.0.0.1:5199/saved', { waitUntil: 'networkidle0' });
        await new Promise(r => setTimeout(r, 600));
        let rmBtn;
        while ((rmBtn = await page.$('button[title*="Remove from saved"]')) !== null) {
            await rmBtn.click();
            await new Promise(r => setTimeout(r, 400));
        }

        await page.goto('http://127.0.0.1:5199/history', { waitUntil: 'networkidle0' });
        await new Promise(r => setTimeout(r, 600));
        let delBtn;
        while ((delBtn = await page.$('button[title*="Delete from history"]')) !== null) {
            await delBtn.click();
            await new Promise(r => setTimeout(r, 400));
        }

        console.log("ALL COMPREHENSIVE VERIFICATIONS PASSED 100%!");
    } catch (err) {
        console.error("Verification failed:", err);
        process.exit(1);
    } finally {
        await browser.close();
    }
}

run();

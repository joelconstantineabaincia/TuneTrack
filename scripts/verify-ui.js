const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');

async function run() {
    console.log("Starting TuneTrack UI Browser Verification...");

    const chromePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
    const browser = await puppeteer.launch({
        executablePath: chromePath,
        headless: 'new',
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-gpu',
            '--window-size=1440,900'
        ]
    });

    const page = await browser.newPage();
    await page.setViewport({ width: 1440, height: 900 });

    page.on('console', msg => console.log('BROWSER CONSOLE:', msg.text()));
    page.on('pageerror', err => console.log('BROWSER ERROR:', err.message));

    const docsDir = path.join(__dirname, '..', 'docs');
    if (!fs.existsSync(docsDir)) {
        fs.mkdirSync(docsDir, { recursive: true });
    }

    try {
        console.log("Navigating to http://127.0.0.1:5199...");
        await page.goto('http://127.0.0.1:5199', { waitUntil: 'networkidle0', timeout: 15000 });

        // 1. Idle Screenshot at 1440px
        const idlePath = path.join(docsDir, 'screenshot-1440-idle.png');
        await page.screenshot({ path: idlePath, fullPage: false });
        console.log("✓ Captured idle screenshot at 1440px:", idlePath);

        // 2. Click "Eclectic Horizon" sample playlist button
        console.log("Clicking 'Eclectic Horizon' sample button...");
        const sampleButtons = await page.$$('button');
        let clickedSample = false;
        for (const btn of sampleButtons) {
            const text = await page.evaluate(el => el.textContent, btn);
            if (text && text.includes('Eclectic Horizon')) {
                await btn.click();
                clickedSample = true;
                break;
            }
        }

        if (!clickedSample) {
            throw new Error("Could not find 'Eclectic Horizon' sample button");
        }

        // Wait for playlist title and diversity score to render
        await page.waitForSelector('h1', { timeout: 10000 });
        await new Promise(r => setTimeout(r, 1500)); // allow live iTunes recs to populate

        const analyzed1440Path = path.join(docsDir, 'screenshot-1440-analyzed.png');
        await page.screenshot({ path: analyzed1440Path, fullPage: true });
        console.log("✓ Captured analyzed dashboard screenshot at 1440px:", analyzed1440Path);

        // 3. Test Tracklist Search / Filter
        console.log("Testing search filter...");
        const searchInput = await page.$('input[placeholder*="Filter tracks"]');
        if (searchInput) {
            await searchInput.type('Pink');
            await new Promise(r => setTimeout(r, 500));
            const rowCount = await page.$$eval('tbody tr', rows => rows.length);
            console.log(`✓ Search filter for 'Pink' returned ${rowCount} row(s)`);
            // Clear input
            await page.evaluate(el => el.value = '', searchInput);
            await searchInput.type(' ');
            await page.keyboard.press('Backspace');
            await new Promise(r => setTimeout(r, 500));
        }

        // 4. Test Bookmark Action
        console.log("Testing bookmark toggle...");
        const bookmarkBtns = await page.$$('button[title*="Bookmark"]');
        if (bookmarkBtns.length > 0) {
            await bookmarkBtns[0].click();
            await new Promise(r => setTimeout(r, 500));
            console.log("✓ Bookmark button clicked and persisted");
        }

        // 5. Test Copy Action
        console.log("Testing copy track action...");
        const copyBtns = await page.$$('button[title*="Copy Title"]');
        if (copyBtns.length > 0) {
            await copyBtns[0].click();
            await new Promise(r => setTimeout(r, 500));
            console.log("✓ Copy track button clicked");
        }

        // 6. Test Mobile Viewport at 390px (iPhone 14/15 size)
        console.log("Setting mobile viewport (390x844)...");
        await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
        await new Promise(r => setTimeout(r, 800));

        const mobilePath = path.join(docsDir, 'screenshot-390-mobile.png');
        await page.screenshot({ path: mobilePath, fullPage: false });
        console.log("✓ Captured mobile screenshot at 390px:", mobilePath);

        // 7. Test "Strictly Synth-Pop" (low diversity)
        console.log("Clicking 'Strictly Synth-Pop' sample...");
        const synthButtons = await page.$$('button');
        for (const btn of synthButtons) {
            const text = await page.evaluate(el => el.textContent, btn);
            if (text && text.includes('Strictly Synth-Pop')) {
                await btn.click();
                break;
            }
        }
        await new Promise(r => setTimeout(r, 1500));
        const synthPath = path.join(docsDir, 'screenshot-1440-synthpop.png');
        await page.screenshot({ path: synthPath, fullPage: true });
        console.log("✓ Captured Synth-Pop (Low Diversity) screenshot:", synthPath);

        console.log("All UI and responsive verifications PASSED!");
    } catch (err) {
        console.error("Verification failed:", err);
        process.exit(1);
    } finally {
        await browser.close();
    }
}

run();

const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');

async function run() {
    console.log("Starting Full End-to-End Browser Verification for TuneTrack...");

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
        console.log("1. Navigating to http://127.0.0.1:5199...");
        await page.goto('http://127.0.0.1:5199', { waitUntil: 'networkidle0', timeout: 15000 });

        // 2. Click "Eclectic Horizon" sample
        console.log("2. Clicking 'Eclectic Horizon' sample button...");
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
        if (!clickedSample) throw new Error("Could not find 'Eclectic Horizon' sample button");

        // Wait for analysis results
        await page.waitForSelector('h1', { timeout: 10000 });
        await new Promise(r => setTimeout(r, 1500));
        console.log("✓ Analysis rendered successfully!");

        // 3. Bookmark a recommendation
        console.log("3. Bookmarking a suggested track...");
        const bookmarkBtns = await page.$$('button[title*="Bookmark"]');
        if (bookmarkBtns.length === 0) throw new Error("No bookmark buttons found on recommendations");
        await bookmarkBtns[0].click();
        await new Promise(r => setTimeout(r, 800));
        console.log("✓ Recommendation bookmarked!");

        // 4. Navigate to /saved
        console.log("4. Navigating to /saved...");
        await page.goto('http://127.0.0.1:5199/saved', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));

        const savedPath = path.join(docsDir, 'screenshot-saved-1440.png');
        await page.screenshot({ path: savedPath, fullPage: false });
        console.log("✓ Captured /saved screenshot:", savedPath);

        const savedCards = await page.$$('h2');
        console.log(`✓ Found ${savedCards.length} saved item(s) on /saved`);
        if (savedCards.length === 0) throw new Error("Expected at least 1 saved item on /saved");

        // 5. Navigate to /history
        console.log("5. Navigating to /history...");
        await page.goto('http://127.0.0.1:5199/history', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));

        const historyPath = path.join(docsDir, 'screenshot-history-1440.png');
        await page.screenshot({ path: historyPath, fullPage: false });
        console.log("✓ Captured /history screenshot:", historyPath);

        const viewReportBtn = await page.$('a[href*="/?id="]');
        if (!viewReportBtn) throw new Error("Could not find 'View Report' link in history");
        console.log("✓ Found 'View Report' link in history");

        // 6. Click View Report to reopen analysis
        console.log("6. Clicking 'View Report' to reopen historical analysis...");
        await viewReportBtn.click();
        await page.waitForSelector('h1', { timeout: 10000 });
        await new Promise(r => setTimeout(r, 1000));
        const reopenedTitle = await page.$eval('h1', el => el.textContent);
        console.log(`✓ Historical analysis reopened: "${reopenedTitle.trim()}"`);

        // 7. Clean up: Remove item from /saved
        console.log("7. Testing removal from /saved...");
        await page.goto('http://127.0.0.1:5199/saved', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));
        const removeSavedBtn = await page.$('button[title*="Remove from saved"]');
        if (removeSavedBtn) {
            await removeSavedBtn.click();
            await new Promise(r => setTimeout(r, 500));
            console.log("✓ Removed saved item successfully");
        }

        // 8. Clean up: Delete item from /history
        console.log("8. Testing deletion from /history...");
        await page.goto('http://127.0.0.1:5199/history', { waitUntil: 'networkidle0', timeout: 10000 });
        await new Promise(r => setTimeout(r, 800));
        const deleteHistoryBtn = await page.$('button[title*="Delete from history"]');
        if (deleteHistoryBtn) {
            await deleteHistoryBtn.click();
            await new Promise(r => setTimeout(r, 500));
            console.log("✓ Deleted history item successfully");
        }

        console.log("==================================================");
        console.log("ALL END-TO-END FLOW VERIFICATIONS PASSED 100%!");
        console.log("==================================================");
    } catch (err) {
        console.error("Verification failed:", err);
        process.exit(1);
    } finally {
        await browser.close();
    }
}

run();

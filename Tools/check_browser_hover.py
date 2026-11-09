"""Check RT1's actual static HTML hover feedback with native Chromium mouse input.
This checks DOM/CSS and the fixture; it does not simulate Android hover dispatch.
"""
import argparse
import json
from pathlib import Path
import re
import shutil
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--browser', default=shutil.which('chromium'))
    args = parser.parse_args()
    source = (ROOT / 'Android~/webview/src/main/java/com/t34400/vrwebview/RuntimeProbeContent.java').read_text()
    # Test the literals actually returned by RT1; the base64 media payload is not
    # needed for hover and has its own decode/provenance tests.
    expression = source.split('return ', 1)[1].split(';\n', 1)[0]
    html = ''.join(json.loads(value) for value in re.findall(r'"(?:[^"\\]|\\.)*"', expression))
    checks = 0
    def check(value, label):
        nonlocal checks
        if not value: raise AssertionError(label)
        checks += 1
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=args.browser, headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
        page = browser.new_page(viewport={'width':1024,'height':768})
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.set_content(html)
        page.evaluate("window.clicks=0; window.inputs=0; document.addEventListener('click',()=>clicks++); document.addEventListener('input',()=>inputs++)")
        box = page.locator('#hover')
        page.mouse.move(1000,740)
        normal = box.evaluate('e=>getComputedStyle(e).backgroundColor')
        box.hover()
        check(box.evaluate('e=>getComputedStyle(e).backgroundColor') != normal, 'CSS :hover did not change')
        check(page.evaluate('hc.over') == 1, 'Native mouse pointerover not counted')
        check(page.evaluate('hc.move') >= 1, 'Native mouse pointermove not counted')
        check(page.evaluate('clicks') == 0, 'Hover generated a click')
        check(page.evaluate('document.activeElement.tagName') not in ('INPUT','TEXTAREA'), 'Hover took editing focus')
        page.locator('#text').fill('Committed 日本語')
        count = page.evaluate('inputs')
        page.mouse.move(1000,740)
        check(box.evaluate('e=>getComputedStyle(e).backgroundColor') == normal, 'CSS hover did not exit')
        check(page.evaluate('hc.out') == 1, 'Native mouse pointerout not counted')
        box.hover()
        check(page.evaluate('hc.over') == 2, 'Hover did not reenter')
        check(page.evaluate('document.activeElement.id') == 'text', 'Hover moved existing editor focus')
        check(page.locator('#text').input_value() == 'Committed 日本語', 'Hover modified text')
        check(page.evaluate('inputs') == count, 'Hover synthesized text input')
        check(not errors, 'RT1 fixture JavaScript failed')
        browser.close()
    print('PASS', checks, 'RT1 DOM/CSS hover checks using native Chromium mouse events')
    print('NOT RUN: Android MotionEvent delivery, Unity EventSystem/XR ray, Quest pointerover/IME')

if __name__ == '__main__': main()

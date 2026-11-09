"""Exercise production editing-end DOM policy with real focus, frames and shadow roots.
This does not run Android InputConnection or Quest IME.
"""
import argparse
from pathlib import Path
import shutil
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / 'Android~/runtime/src/main/assets/vrwebview-end-editing.js'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--browser', default=shutil.which('chromium'))
    args = parser.parse_args()
    checks = 0
    def check(value, label):
        nonlocal checks
        if not value: raise AssertionError(label)
        checks += 1
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=args.browser, headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
        context = browser.new_context()
        context.route('**/*', lambda route: route.fulfill(status=200, content_type='text/html', body="<input id='frame'>"))
        page = context.new_page()
        focus_session = context.new_cdp_session(page)
        focus_session.send('Emulation.setFocusEmulationEnabled', {'enabled':False})
        page.goto('https://sample.invalid/')
        page.set_content("""<input id='text'><input id='password' type='password'><textarea id='multi'></textarea>
          <div id='editable' contenteditable>Initial</div><input id='readonly' readonly value='Read only'>
          <button id='button'>Button</button><div id='shadow'></div>
          <iframe id='same' src='/frame'></iframe><iframe id='cross' src='https://other.invalid/frame'></iframe>
          <script>window.inputEvents=0;document.addEventListener('input',()=>inputEvents++);
            document.getElementById('shadow').attachShadow({mode:'open'}).innerHTML='<input id="inner">';</script>""")
        focus_session.send('Emulation.setFocusEmulationEnabled', {'enabled':False})
        handoff = context.new_page()
        host_session = context.new_cdp_session(handoff)
        host_session.send('Emulation.setFocusEmulationEnabled', {'enabled':False})
        handoff.set_content('<button>Host</button>')
        script = SCRIPT.read_text()
        def focus_host():
            focus_session.send('Emulation.setFocusEmulationEnabled', {'enabled':False})
            host_session.send('Emulation.setFocusEmulationEnabled', {'enabled':False})
            handoff.bring_to_front(); handoff.locator('button').click()
            page.wait_for_function('!document.hasFocus()', polling=50, timeout=5000)
        def focus_page():
            page.bring_to_front()
        for selector in ['#text','#password','#multi','#editable','#inner']:
            focus_page(); editor = page.locator(selector); editor.fill('Committed 日本語'); editor.focus()
            count = page.evaluate('inputEvents')
            check(not page.evaluate(script), 'Cleanup cleared a currently focused editor')
            focus_host(); check(page.evaluate(script), 'Background editor not cleared: '+selector)
            check(not page.evaluate(script), 'Cleanup was not idempotent')
            check(editor.input_value() == 'Committed 日本語' if selector!='#editable' else editor.inner_text() == 'Committed 日本語', 'Committed text changed')
            check(page.evaluate('inputEvents') == count, 'Cleanup synthesized input')
        focus_page(); page.locator('#readonly').focus(); focus_host()
        check(not page.evaluate(script), 'Read-only field was blurred')
        focus_page(); page.locator('#button').focus(); focus_host()
        check(not page.evaluate(script), 'Noneditor was blurred')
        focus_page(); same = page.frame_locator('#same').locator('#frame'); same.fill('Same-origin 日本語'); same.focus(); focus_host()
        check(page.evaluate(script), 'Same-origin nested editor not cleared')
        check(same.input_value() == 'Same-origin 日本語', 'Same-origin text changed')
        focus_page(); cross = page.frame_locator('#cross').locator('#frame'); cross.fill('Cross-origin'); cross.focus(); focus_host()
        check(not page.evaluate(script), 'Cross-origin boundary was crossed or the frame was blurred')
        check(cross.input_value() == 'Cross-origin', 'Cross-origin content changed')
        focus_page(); page.locator('#text').focus()
        check(not page.evaluate(script), 'Late callback cleared reentered editing')
        browser.close()
    print('PASS', checks, 'production DOM editing-end checks (real focus, preserved text/events, shadow DOM, same/cross-origin frames)')
    print('NOT RUN: Android IME/Window focus, Unity compilation, Quest rendering')

if __name__ == '__main__': main()

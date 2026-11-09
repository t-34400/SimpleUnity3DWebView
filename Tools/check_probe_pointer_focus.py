"""Check the controlled probe page's pointer/focus behavior in Chromium, not Quest IME."""
import argparse
from pathlib import Path
import shutil
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Android~/webview/src/main/assets'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--browser', default=shutil.which('chromium'))
    args = parser.parse_args()
    checks = 0

    def check(condition, description):
        nonlocal checks
        if not condition:
            raise AssertionError(description)
        checks += 1

    html = (ASSETS / 'vrwebview-proxy-probe.html').read_text()
    video = (ASSETS / 'vrwebview-probe-video.mp4').read_bytes()
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=args.browser, headless=True,
                                    args=['--no-sandbox', '--disable-dev-shm-usage'])
        context = browser.new_context(viewport={'width': 1024, 'height': 768}, has_touch=True, is_mobile=True)
        page = context.new_page()
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))

        def route(request):
            if request.request.url == 'https://vrwebview.invalid/probe/index.html':
                request.fulfill(status=200, content_type='text/html', body=html)
            elif request.request.url == 'https://vrwebview.invalid/probe/video.mp4':
                request.fulfill(status=200, content_type='video/mp4', body=video)
            else:
                request.fulfill(status=403, body='')

        page.route('**/*', route)
        page.goto('https://vrwebview.invalid/probe/index.html')
        page.wait_for_function("typeof window.vrwebviewProxyMetadata==='function'")
        # Registered after the page's capture listener: observe its result before default focus actions.
        page.evaluate("""document.addEventListener('pointerdown', event => {
          window.beforePointerDefault = {editable:window.vrwebviewProxyMetadata().editable,
            prevented:event.defaultPrevented};
        }, true)""")
        meta = lambda: page.evaluate('window.vrwebviewProxyMetadata()')
        page.locator('#text').tap()
        check(meta()['editable'], 'Real touch must focus the text editor')
        page.locator('#text').fill('Synthetic 日本語')
        input_count = meta()['input']
        page.locator('#play').tap()
        page.wait_for_function('!document.getElementById("video").paused')
        page.wait_for_function('document.getElementById("video").currentTime>0.02')
        check(not page.evaluate('window.beforePointerDefault.editable'), 'Blur must happen before default pointer processing')
        check(not page.evaluate('window.beforePointerDefault.prevented'), 'Playback user activation/default events must survive')
        check(meta()['blurredEditors'] == 1 and not meta()['editable'], 'Playback button must release the previous editor')
        check(meta()['input'] == input_count, 'Blur must not synthesize input or copy text')
        check(page.locator('#text').input_value() == 'Synthetic 日本語', 'Committed text must be preserved')

        # The native video control path is retargeted through the media element.
        page.locator('#password').focus()
        page.locator('#video').tap(position={'x': 40, 'y': 30})
        check(meta()['pointerTarget'] == 'media' and not meta()['editable'], 'Media surface tap must release stale editor focus')
        check(meta()['blurredEditors'] == 2, 'Media tap must blur exactly one editor')
        page.locator('#password').focus()
        box = page.locator('#video').bounding_box()
        page.locator('#video').tap(position={'x': 20, 'y': box['height'] - 15})
        check(meta()['pointerTarget'] == 'media' and not meta()['editable'], 'Native video control area must release stale editor focus')
        check(meta()['blurredEditors'] == 3, 'Native media control hit must not retain the old editor')

        page.locator('#text').tap()
        count = meta()['blurredEditors']
        label = page.locator('label').filter(has=page.locator('#text'))
        label.tap(position={'x': 5, 'y': 4})
        check(meta()['blurredEditors'] == count and meta()['pointerTarget'] == 'editor', 'Associated label must preserve editing')
        check(page.evaluate('document.activeElement===document.getElementById("text")'), 'Label default behavior must still focus its field')
        page.locator('#second').tap()
        check(page.evaluate('document.activeElement===document.getElementById("second")'), 'Changing fields must keep native DOM focus behavior')
        check(meta()['blurredEditors'] == count, 'Switching editors must not force an intermediate blur')
        page.locator('#multiline').tap()
        check(meta()['editable'], 'Textarea must remain editable')
        page.evaluate("document.getElementById('editable').innerHTML='<span id=editor-child>Editable sample</span>'")
        page.locator('#editor-child').tap()
        check(meta()['editable'] and meta()['pointerTarget'] == 'editor', 'Nested contenteditable target must remain editable')

        # Browser-created trusted input vs page-dispatched synthetic events.
        page.locator('#text').focus()
        count = meta()['pointerDown']
        page.evaluate("document.getElementById('video').dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,composed:true}))")
        check(meta()['editable'] and meta()['pointerDown'] == count, 'Synthetic DOM event must not end an editing session')
        page.evaluate("""document.body.insertAdjacentHTML('beforeend',
          '<input id=read-only readonly><input id=checkbox type=checkbox>')""")
        page.locator('#read-only').tap()
        check(not meta()['editable'], 'Readonly target is not a text editor')
        page.locator('#text').focus()
        count = meta()['blurredEditors']
        page.locator('#checkbox').tap()
        check(not meta()['editable'] and meta()['blurredEditors'] == count + 1, 'Checkbox must release the previous text editor')
        check(page.locator('#checkbox').is_checked(), 'Checkbox default action must survive')
        check(meta()['pointerPolicy'] == 1, 'New page revision must be detectable from metadata')
        check(not errors, 'Page JavaScript must not throw')
        context.close()
        browser.close()
    print(f'PASS {checks} Chromium touch/focus checks, real playback and native media-surface hit')
    print('NOT RUN: Android WebView provider, Quest keyboard/composition, Unity GPU and ray recovery')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Browser regression checks against dashboard HTML and fixtures exported by WebPanelSmoke.

Requires playwright and Chromium. All HTTP requests are intercepted; no real server is changed.
"""
import argparse
import asyncio
import copy
import json
import re
from pathlib import Path
from urllib.parse import urlparse, parse_qs, quote
from playwright.async_api import async_playwright


async def check(args):
    html = Path(args.html).read_text()
    fixtures = json.loads(Path(args.fixtures).read_text())
    token = 'review-token<&"'
    posts, dialogs, errors, requests = [], [], [], []
    state = {'fail_actions': False, 'reset_events': False}
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(executable_path=args.browser, headless=True, args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width': 1280, 'height': 900})
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.on('dialog', lambda dialog: (dialogs.append(dialog.message), asyncio.create_task(dialog.accept())))

        async def route_request(route):
            request = route.request
            url = urlparse(request.url)
            if url.path == '/':
                await route.fulfill(body=html, content_type='text/html')
                return
            if request.headers.get('authorization') != 'Bearer ' + token:
                await route.fulfill(status=401, json={'error': 'Invalid token'})
                return
            requests.append(url.path)
            if request.method == 'POST':
                posts.append((url.path, request.post_data_json))
                if state['fail_actions']:
                    await route.fulfill(status=503, json={'error': 'Acción rechazada de prueba'})
                else:
                    await route.fulfill(json={'Ok': True, 'Message': 'Acción de prueba completada'})
                return
            key = {'/api/status': 'status', '/api/v1/host/clock': 'clock', '/api/v1/host/players': 'players',
                   '/api/v1/host/festival': 'festival', '/api/v1/host/events': 'events', '/api/v1/host/map': 'map'}.get(url.path)
            if key is None:
                await route.fulfill(status=404, json={'error': 'Not found'})
                return
            payload = copy.deepcopy(fixtures[key])
            if key == 'events':
                if state['reset_events']:
                    payload = {'SinceId': 100, 'LastEventId': 0, 'Events': []}
                elif int(parse_qs(url.query).get('since', ['0'])[0]) >= 1:
                    payload['Events'] = []
            if key == 'map':
                location = parse_qs(url.query).get('location', ['Town'])[0]
                payload['LocationName'] = location
                if location == 'Farm':
                    await asyncio.sleep(.2)
            await route.fulfill(json=payload)

        await page.route('**/*', route_request)
        await page.goto('http://panel.test/?token=' + quote(token, safe=''))
        await page.wait_for_function("document.getElementById('kpi-online').textContent === '2'")
        assert 'token=' not in page.url
        assert await page.locator('#tokenInput').input_value() == token
        assert await page.locator('.tab-nav [role=tab]').count() == 6
        await page.wait_for_function("document.getElementById('eventLog').textContent.includes('Evento de prueba')")
        assert 'Evento de prueba' in await page.locator('#eventLog').inner_text()
        assert '2026-11-06T14:30:00Z' in await page.locator('#eventLog').inner_text()
        await page.evaluate('Promise.all([fetchEvents(), fetchEvents()])')
        assert await page.locator('#eventLog .event-item').count() == 1
        assert await page.locator('#playersTable img').count() == 0
        assert await page.evaluate('window.xss || 0') == 0
        assert await page.locator('#playersTable tr td').nth(6).inner_text() == '2'

        await page.get_by_role('tab', name=re.compile('Mapa')).click()
        await page.wait_for_function("MAP.data && document.getElementById('mapLoadOverlay').style.display === 'none'")
        assert await page.locator('#mapCanvas').evaluate('(c) => c.width > 0 && c.height > 0')
        assert await page.evaluate('Array.from(MAP.data.Tiles).slice(0,4)') == [1, 3, 8, 11]
        pixel = await page.evaluate("Array.from(MAP.offscreen.getContext('2d').getImageData(4,4,1,1).data)")
        assert pixel[:3] == [58, 74, 58], pixel
        assert await page.locator('#mapLocationSelect option[onclick]').count() == 0
        box = await page.locator('#mapViewport').bounding_box()
        await page.mouse.move(box['x'] + 150, box['y'] + 150)
        zoom_before = await page.evaluate('MAP.zoom')
        await page.mouse.wheel(0, -200)
        await page.wait_for_function(f'MAP.zoom > {zoom_before}')
        pan_before = await page.evaluate('[MAP.panX, MAP.panY]')
        await page.mouse.down()
        await page.mouse.move(box['x'] + 190, box['y'] + 180)
        await page.mouse.up()
        pan_after = await page.evaluate('[MAP.panX, MAP.panY]')
        assert abs(pan_after[0]-pan_before[0]-40)<1 and abs(pan_after[1]-pan_before[1]-30)<1
        await page.evaluate('MAP.zoom=4; MAP.panX=123; MAP.panY=47; loadMap()')
        assert await page.evaluate('[MAP.zoom, MAP.panX, MAP.panY]') == [4, 123, 47]
        await page.evaluate("""async () => {
            const selector=document.getElementById('mapLocationSelect');
            selector.value='Farm'; const older=loadMap();
            selector.value='Town'; const newer=loadMap();
            await Promise.all([older,newer]);
        }""")
        assert await page.evaluate('MAP.data.LocationName') == 'Town'
        await page.evaluate('mapResetView()')
        Path(args.output).mkdir(parents=True, exist_ok=True)
        await page.screenshot(path=str(Path(args.output) / 'dashboard-map.png'), full_page=True)

        await page.get_by_role('tab', name=re.compile('Acciones')).click()
        await page.locator('#restartReason').fill('Prueba segura')
        await page.evaluate('scheduleRestart()')
        assert posts[-1][0] == '/api/v1/host/restart'
        assert posts[-1][1]['Immediate'] is False and posts[-1][1]['WarningMinutes'] is None
        before = len(posts)
        await page.locator('#restartWarnMins').fill('-1')
        await page.evaluate('scheduleRestart()')
        assert len(posts) == before
        await page.locator('#restartWarnMins').fill('5')
        await page.evaluate('scheduleRestart()')
        assert posts[-1][1]['WarningMinutes'] == 5 and posts[-1][1]['Immediate'] is False
        state['fail_actions'] = True
        await page.evaluate("callAction('/api/mines/reset')")
        assert 'Acción rechazada de prueba' in dialogs[-1]
        state['reset_events'] = True
        await page.evaluate('fetchEvents()')
        assert await page.evaluate('lastEventCursor') == 0
        state['reset_events'] = False
        await page.evaluate('fetchEvents()')
        assert await page.locator('#eventLog .event-item').count() == 1

        fixtures['events'] = {'SinceId': 0, 'LastEventId': 100, 'Events': [
            {'Id': i, 'TimestampUtc': '2026-11-06T14:30:00Z', 'Summary': f'Evento {i}'} for i in range(1,101)]}
        await page.evaluate("lastEventCursor=0; document.getElementById('eventLog').replaceChildren(); fetchEvents()")
        assert await page.locator('#eventLog .event-item').count() == 80
        assert 'Evento 100' in await page.locator('#eventLog .event-item').first.inner_text()
        assert 'Evento 21' in await page.locator('#eventLog .event-item').last.inner_text()

        await page.set_viewport_size({'width': 390, 'height': 844})
        await page.get_by_role('tab', name=re.compile('Mapa')).click()
        await page.wait_for_function("document.getElementById('mapLoadOverlay').style.display === 'none'")
        assert await page.locator('#mapCanvas').evaluate('(c) => c.width > 0 && c.height > 0')
        assert await page.evaluate('document.documentElement.scrollWidth <= window.innerWidth + 1')
        assert await page.evaluate("""() => {
            const vp=document.getElementById('mapViewport'), rect=vp.getBoundingClientRect();
            const t=(id,x)=>new Touch({identifier:id,target:vp,clientX:rect.left+x,clientY:rect.top+200});
            vp.dispatchEvent(new TouchEvent('touchstart',{touches:[t(1,100),t(2,150)],cancelable:true}));
            const before=MAP.zoom;
            vp.dispatchEvent(new TouchEvent('touchmove',{touches:[t(1,75),t(2,175)],cancelable:true}));
            vp.dispatchEvent(new TouchEvent('touchend',{touches:[],cancelable:true}));
            return MAP.zoom>before;
        }""")
        await page.screenshot(path=str(Path(args.output) / 'dashboard-mobile.png'), full_page=True)
        assert not errors, errors
        await browser.close()
    print('PASS: auth bootstrap, six tabs, DTO/base64 rendering, events/cursor reset, XSS escaping, map sizing/view/race, safe restart, API errors, mobile layout')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--html', required=True)
    parser.add_argument('--fixtures', required=True)
    parser.add_argument('--browser')
    parser.add_argument('--output', default='/tmp/p4e-web-panel-review')
    asyncio.run(check(parser.parse_args()))

const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../src/UniversitySchedule.Web/wwwroot/js/browser-services.js'), 'utf8');

function setup({permission = 'granted', supported = true, active = true} = {}) {
    const timers = new Map(), shown = [], warnings = [];
    let next = 0;
    const registration = {active, showNotification: async (title, options) => shown.push({title, options})};
    const sandbox = {URL, Date, Map, Promise, console: {warn: (...args) => warnings.push(args)},
        document: {baseURI: 'https://example.test/cfu/'},
        navigator: {serviceWorker: {getRegistration: async () => registration}},
        setTimeout: cb => {timers.set(++next, cb);return next;}, clearTimeout: id => timers.delete(id),
        Notification: class {static permission = permission; static async requestPermission() {return permission;} constructor() {throw Error('Mobile Notification constructor must not be called');}}
    };
    sandbox.window = sandbox;
    if (!supported) delete sandbox.Notification;
    vm.runInNewContext(source, sandbox);
    return {api:sandbox.cfuNotifications,timers,shown,warnings,registration};
}
const item = {id:'task-1', subject:'Алгоритмы',text:'Задача', triggerAt:new Date(Date.now()+60000).toISOString(),deadline:new Date(Date.now()+3600000).toISOString()};

test('mobile reminder uses service worker and handles revocation/errors',async()=>{
    const s=setup();
    assert.equal(await s.api.requestPermission(),'granted');
    s.api.schedule([item]);
    await [...s.timers.values()][0]();
    assert.equal(s.shown.length,1);
    assert.equal(s.shown[0].options.tag,'cfu-assignment-task-1');
    assert.match(s.shown[0].options.body,/МСК/);
    s.registration.showNotification=async()=>{throw new Error('permission revoked');};
    s.api.schedule([item]);
    await [...s.timers.values()].at(-1)();
    assert.equal(s.warnings.length,1);
});
test('completed/deleted assignment cancels even an already queued callback',async()=>{
    const s=setup();s.api.schedule([item]);const queued=[...s.timers.values()][0];
    s.api.schedule([]);await queued();assert.equal(s.shown.length,0);
});
test('unsupported, denied and not-yet-active worker return distinct statuses',async()=>{
    assert.equal(await setup({supported:false}).api.requestPermission(),'unsupported');
    assert.equal(await setup({permission:'denied'}).api.requestPermission(),'denied');
    assert.equal(await setup({active:false}).api.requestPermission(),'not-ready');
});

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const express = require('express');
process.env.ADMIN_USER_IDS = '91000001';
process.env.ADMIN_JWT_SECRET = 'profile-tests-local-only';
process.env.ADMIN_JWT_EXPIRES_SEC = '60';
process.env.BOT_API_JWT_SECRET = 'profile-tests-bot-local-only';
const { requireAdmin } = require('../../middleware/requireAdmin');
const { signToken } = require('../../utils/jwt');
const config = require('../../config/config');

function loadRouter(relative, imports) {
  const filename = path.join(__dirname, relative);
  const sandbox = { module: { exports: {} }, console: { error() {} }, require(name) {
    if (name === 'express') return express;
    if (name === '../../services/venueIcons') return require('../../services/venueIcons');
    if (Object.hasOwn(imports, name)) return imports[name];
    throw new Error(`Unexpected import ${name}`);
  } };
  vm.runInNewContext(fs.readFileSync(filename, 'utf8'), sandbox, { filename });
  return sandbox.module.exports;
}

async function withServer(app, callback) {
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  try { await callback(`http://127.0.0.1:${server.address().port}`); }
  finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
}

function setupAdmin(kind = 'event') {
  const state = {
    event: { event_id: 'venue-test', kind, name: '原名称', description: '原介绍', status: 'active', reopen_requested: false },
    audit: [], connections: 0, released: 0, failAudit: false,
  };
  const client = {
    async query(sql, params = []) {
      if (sql === 'BEGIN') { state.before = { ...state.event }; return { rows: [] }; }
      if (sql === 'COMMIT') { state.before = null; return { rows: [] }; }
      if (sql === 'ROLLBACK') { if (state.before) state.event = state.before; state.before = null; return { rows: [] }; }
      if (sql.startsWith('SELECT')) return { rows: params[0] === state.event.event_id ? [{ ...state.event }] : [] };
      if (sql.startsWith('UPDATE events')) {
        state.event = { ...state.event, name: params[0], description: params[1], team_icon_url: params[3] };
        return { rows: [{ ...state.event }] };
      }
      throw new Error(`Unexpected query ${sql}`);
    },
    release() { state.released += 1; },
  };
  const router = loadRouter('events.js', {
    '../../config/database': { async connect() { state.connections += 1; return client; } },
    '../../config/config': { calcServer: { baseUrl: 'http://127.0.0.1:8081', timeoutMs: 1000 } },
    '../../utils/audit': { async writeAudit(audit, db) {
      assert.equal(db, client, 'profile update and audit must use the same transaction');
      if (state.failAudit) throw new Error('audit failed');
      state.audit.push(audit);
    } },
    '../../utils/eventsTables': {},
    '../../services/eventPlayerStats': {},
    '../../services/duplicateWalls': { createDuplicateWallService: () => ({}) },
    '../../services/eventRoomCreation': { createEventRoomCreationService: () => ({}) },
    '../../services/eventDuplicateWalls': { createEventDuplicateWallService: () => ({}) },
    '../../services/eventRoomBots': { createEventRoomBotService: () => ({}) },
    '../../services/gameServerProxy': {},
  });
  const app = express();
  app.use(express.json());
  app.use('/events', requireAdmin, router);
  const token = signToken({ user_id: 91000001, username: 'test-admin' }, config.admin.jwtSecret, 60);
  const put = (base, body, eventId = 'venue-test', auth = token) => fetch(`${base}/events/${eventId}/profile`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json', ...(auth ? { Authorization: `Bearer ${auth}` } : {}) },
    body: JSON.stringify(body),
  });
  return { state, app, put };
}

for (const kind of ['event', 'base']) {
  test(`administrator updates ${kind} profile directly, preserving lifecycle and recording before/after`, async () => {
    const { state, app, put } = setupAdmin(kind);
    await withServer(app, async base => {
      const response = await put(base, { name: ' 新名称 ', description: ' 新介绍\n第二行 ', reason: ' 修正介绍 ', kind: 'ignored', status: 'closed' });
      assert.equal(response.status, 200);
      const { data } = await response.json();
      assert.equal(data.event.name, '新名称');
      assert.equal(data.event.description, '新介绍\n第二行');
      assert.equal(data.event.kind, kind);
      assert.equal(data.event.status, 'active');
      assert.equal(state.audit.length, 1);
      assert.equal(state.audit[0].adminUserId, 91000001);
      assert.equal(state.audit[0].payload.before.name, '原名称');
      assert.equal(state.audit[0].payload.after.name, '新名称');
      assert.equal(state.audit[0].reason, '修正介绍');
      assert.equal(state.released, 1);
    });
  });
}

test('profile update rejects unauthenticated/non-admin requests and invalid inputs without mutations', async () => {
  const { state, app, put } = setupAdmin();
  await withServer(app, async base => {
    const valid = { name: '新名称', description: '新介绍', reason: '修正' };
    assert.equal((await put(base, valid, 'venue-test', '')).status, 401);
    const playerToken = signToken({ user_id: 91000002 }, config.admin.jwtSecret, 60);
    assert.equal((await put(base, valid, 'venue-test', playerToken)).status, 403);
    for (const patch of [{ name: ' ' }, { name: '名'.repeat(129) }, { description: '文'.repeat(2001) }, { reason: '' }]) {
      assert.equal((await put(base, { ...valid, ...patch })).status, 400);
    }
    assert.equal(state.connections, 0);
    assert.equal((await put(base, valid, 'missing')).status, 404);
    assert.equal((await put(base, { name: '原名称', description: '原介绍', reason: '无变化' })).status, 400);
    assert.equal(state.event.name, '原名称');
    assert.equal(state.audit.length, 0);
  });
});

test('profile update rolls back when the audit cannot be saved', async () => {
  const { state, app, put } = setupAdmin();
  state.failAudit = true;
  await withServer(app, async base => {
    assert.equal((await put(base, { name: '新名称', description: '新介绍', reason: '修正' })).status, 500);
    assert.equal(state.event.name, '原名称');
    assert.equal(state.event.description, '原介绍');
    assert.equal(state.released, 1);
  });
});

test('only the latest rejected profile request is offered for editing, never an older rejection', async () => {
  let latestStatus = 'rejected';
  const router = loadRouter('../event-admin/announcements.js', {
    '../../config/database': { async query(sql) {
      if (sql.includes("r.status = 'pending'")) return { rows: [] };
      assert.doesNotMatch(sql, /WHERE[^`]*r.status = 'rejected'/);
      return { rows: [{ request_id: 2, status: latestStatus, proposed_name: '拟改名称', proposed_description: '拟改介绍', review_note: '请补充资料' }] };
    } },
    '../../utils/audit': {},
    '../../middleware/requireEventAdmin': {
      requireEventMembership(req, _res, next) { req.event = { event_id: 'venue-test', name: '当前名称', description: '当前介绍' }; next(); },
      requireEventOwner(_req, _res, next) { next(); },
    },
  });
  const app = express();
  app.use('/events/:eventId', router);
  await withServer(app, async base => {
    const read = async () => (await (await fetch(`${base}/events/venue-test/profile-change`)).json()).data;
    assert.equal((await read()).last_rejected.proposed_name, '拟改名称');
    latestStatus = 'approved';
    assert.equal((await read()).last_rejected, null);
    latestStatus = 'cancelled';
    assert.equal((await read()).last_rejected, null);
  });
});


test('icon-only removal is a profile change; old clients preserve the current icon', async () => {
  const { state, app, put } = setupAdmin();
  state.event.team_icon_url = '/api/venue-icons/' + 'a'.repeat(64) + '.png';
  await withServer(app, async base => {
    let response = await put(base, { name:'新名称', description:'原介绍', reason:'保留图标' });
    assert.equal(response.status, 200);
    assert.ok(state.event.team_icon_url.endsWith('.png'));
    response = await put(base, { name:'新名称', description:'原介绍', reason:'移除图标', team_icon_url:'' });
    assert.equal(response.status, 200);
    assert.equal(state.event.team_icon_url, '');
    response = await put(base, { name:'新名称', description:'原介绍', reason:'非法图标', team_icon_url:'https://example.com/logo.svg' });
    assert.equal(response.status, 400);
    assert.equal(state.event.team_icon_url, '');
  });
});

for (const kind of ['event', 'base']) {
  test(`${kind} applications support optional uploads, retaining omitted icons and explicitly clearing them`, async () => {
    const old = process.env.OM_DATA_DIR;
    process.env.OM_DATA_DIR = fs.mkdtempSync(path.join(process.env.OM_TEST_ARTIFACTS || require('os').tmpdir(), 'venue-flow-'));
    let row;
    const router = loadRouter('../player/eventApplications.js', {
      '../../middleware/requirePlayer': { requirePlayer(req, _res, next) { req.player={userId:91000002}; next(); } },
      '../../utils/applicationRemarks': require('../../utils/applicationRemarks'),
      '../../config/database': { async query(sql, params) {
        if (sql.startsWith('SELECT application_id')) return { rows: [] };
        if (sql.startsWith('SELECT *')) return { rows: [row] };
        if (sql.startsWith('INSERT')) {
          assert.match(sql, /remark_history, team_icon_url/);
          row={application_id:1,status:'pending',kind,team_icon_url:params[10],name:params[1],description:params[2]};
        } else if (sql.startsWith('UPDATE')) {
          assert.match(sql, /team_icon_url = COALESCE\(\$12, team_icon_url\)/);
          row={...row, team_icon_url:params[11] ?? row.team_icon_url};
        } else throw new Error(sql);
        return {rows:[row]};
      } },
    });
    const app=express();app.use(express.json());app.use('/applications',router);
    const body={name:'队徽测试',description:'介绍',organizer_name:'测试负责人',organizer_phone:'13800000000',planned_start_at:'2027-01-01',kind};
    const png=fs.readFileSync(path.join(__dirname,'../../../../open_mahjong_unity/Assets/Resources/image/logo.png'));
    const data='data:image/png;base64,'+png.toString('base64');
    try { await withServer(app,async base=>{
      const send=async(method,suffix,patch={})=>{const r=await fetch(base+'/applications'+suffix,{method,headers:{'Content-Type':'application/json'},body:JSON.stringify({...body,...patch})});assert.equal(r.status,200);return (await r.json()).data;};
      assert.equal((await send('POST','')).team_icon_url,'');
      const uploaded=(await send('POST','',{team_icon_url:data})).team_icon_url;
      assert.match(uploaded,/^\/api\/venue-icons\/[a-f0-9]{64}\.png$/);
      assert.equal((await send('PUT','/1')).team_icon_url,uploaded);
      assert.equal((await send('PUT','/1',{team_icon_url:''})).team_icon_url,'');
      row.team_icon_url=uploaded;row.status='rejected';
      assert.equal((await send('POST','/1/resubmit')).team_icon_url,uploaded);
      assert.equal((await send('POST','/1/resubmit',{team_icon_url:''})).team_icon_url,'');
    }); } finally {if(old===undefined)delete process.env.OM_DATA_DIR;else process.env.OM_DATA_DIR=old;}
  });
}

test('application approval copies the uploaded icon into the created venue',async()=>{
  for(const icon of ['', '/api/venue-icons/'+'a'.repeat(64)+'.png']) {
    let saved;
    const row={application_id:1,status:'pending',applicant_user_id:91000002,name:'测试',description:'介绍',kind:'event',team_icon_url:icon};
    const client={release(){},async query(sql,p=[]){
      if(sql.startsWith('SELECT * FROM event_applications'))return {rows:[row]};
      if(sql.startsWith('SELECT user_id'))return {rows:[{user_id:91000002,is_tourist:false}]};
      if(sql.startsWith('INSERT INTO events')){assert.match(sql,/team_icon_url/);saved=p[6];}
      return {rows:sql.startsWith('UPDATE event_applications')?[{...row,status:'approved'}]:[]};
    }};
    const router=loadRouter('eventApplications.js',{'../../config/database':{async connect(){return client;}},'../../utils/audit':{async writeAudit(){}},'../../utils/eventsTables':{generateEventId:()=> 'test-venue'},'../../utils/applicationRemarks':require('../../utils/applicationRemarks')});
    const app=express();app.use(express.json());app.use((req,_res,next)=>{req.admin={userId:91000001};next();});app.use('/review',router);
    await withServer(app,async base=>{assert.equal((await fetch(base+'/review/1/approve',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status,200);assert.equal(saved,icon);});
  }
});

test('profile review applies removal but preserves icons on legacy requests',async()=>{
  for(const proposed of ['',null]) {
    const current='/api/venue-icons/'+'a'.repeat(64)+'.png';let saved;
    const row={request_id:1,event_id:'venue-test',status:'pending',proposed_name:'测试',proposed_description:'介绍',proposed_team_icon_url:proposed};
    const client={release(){},async query(sql,p=[]){
      if(sql.startsWith('SELECT * FROM event_profile'))return {rows:[row]};
      if(sql.startsWith('SELECT event_id'))return {rows:[{event_id:'venue-test',team_icon_url:current}]};
      if(sql.startsWith('UPDATE events')){assert.match(sql,/team_icon_url = COALESCE\(\$4, team_icon_url\)/);saved=p[3]??current;return {rows:[{team_icon_url:saved}]};}
      return {rows:sql.startsWith('UPDATE event_profile')?[row]:[]};
    }};
    const router=loadRouter('eventProfileChanges.js',{'../../config/database':{async connect(){return client;}},'../../utils/audit':{async writeAudit(){}}});
    const app=express();app.use(express.json());app.use((req,_res,next)=>{req.admin={userId:91000001};next();});app.use('/review',router);
    await withServer(app,async base=>{assert.equal((await fetch(base+'/review/1/approve',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({reason:'测试审核'})})).status,200);assert.equal(saved,proposed??current);});
  }
});

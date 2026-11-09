const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function harness(fetch) {
  const routes = {};
  const router = { post: (p, fn) => { routes[`POST ${p}`] = fn; }, get: (p, fn) => { routes[`GET ${p}`] = fn; } };
  const module = { exports: {} };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'mailbox.js'),'utf8'), {
    module, require: name => name === 'express' ? { Router: () => router } : { calcServer: {baseUrl:'http://localhost:8081/'} },
    fetch, AbortSignal, console: { error() {} },
  });
  return async (route, changes = {}) => {
    const req = { method:route.split(' ')[0], headers:{authorization:'Bearer test-admin'}, params:{userId:'101'}, body:{request_id:'original-request',user_id:101,rewards:[]}, ...changes };
    const res = { statusCode:200, status(code) { this.statusCode=code;return this; }, json(data) { this.body=data;return this; } };
    await routes[route](req,res); return res;
  };
}
test('forwards admin identity and original delivery ID to game mail, not SMTP', async () => {
  const invoke = harness(async (url, options) => {
    assert.equal(url,'http://localhost:8081/admin/mail');
    assert.equal(options.headers.Authorization,'Bearer test-admin');
    assert.equal(JSON.parse(options.body).request_id,'original-request');
    return {ok:true,status:200,json:async () => ({success:true,data:{duplicate:true}})};
  });
  const res=await invoke('POST /send');assert.equal(res.body.data.duplicate,true);
});
test('lists only the requested user and rejects malformed IDs', async () => {
  let calls=0;
  const invoke=harness(async (url,options) => { calls++;assert.equal(url,'http://localhost:8081/admin/mail/users/101');assert.equal(options.body,undefined);return {ok:true,status:200,json:async()=>({success:true,data:{mails:[]}})}; });
  assert.equal((await invoke('GET /users/:userId')).statusCode,200);
  assert.equal((await invoke('GET /users/:userId',{params:{userId:'../inventory'}})).statusCode,400);
  assert.equal(calls,1);
});
test('preserves rejection status and reports uncertain results as retryable', async () => {
  const reject=harness(async()=>({ok:false,status:409,json:async()=>({detail:'投递编号已用于其他邮件'})}));
  assert.equal((await reject('POST /send')).statusCode,409);
  const timeout=harness(async()=>{throw new Error('timeout');});
  const res=await timeout('POST /send');assert.equal(res.statusCode,503);assert.equal(res.body.retryable,true);
});

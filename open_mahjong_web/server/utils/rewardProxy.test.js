const test = require('node:test');
const assert = require('node:assert/strict');
const express = require('express');
const config = require('../config/config');
const rewardProxy = require('./rewardProxy');

test('reward gateway forwards identity, query, and request ID; exposes retryable uncertainty', async () => {
  const upstream = express(); upstream.use(express.json());
  const received = [];
  upstream.use((req, res) => {
    received.push({ url:req.url, method:req.method, token:req.headers.authorization, body:req.body });
    if (req.url.includes('unavailable')) return res.status(503).json({ detail:'服务维护' });
    if (req.url.includes('invalid')) return res.status(409).json({ detail:'已领取' });
    res.json({ success:true,data:{request_id:req.body?.request_id} });
  });
  const first = await new Promise(resolve => { const s=upstream.listen(0,'127.0.0.1',()=>resolve(s)); });
  const previous=config.calcServer.baseUrl;
  config.calcServer.baseUrl=`http://127.0.0.1:${first.address().port}`;
  const gateway=express();gateway.use(express.json());gateway.use('/gateway',rewardProxy('/admin/redemption'));
  const second=await new Promise(resolve => {const s=gateway.listen(0,'127.0.0.1',()=>resolve(s));});
  const base=`http://127.0.0.1:${second.address().port}/gateway`;
  try {
    let res=await fetch(base+'/campaigns?page=3',{headers:{Authorization:'Bearer test-identity'}});
    assert.equal(res.status,200);assert.equal(res.headers.get('cache-control'),'no-store');
    assert.deepEqual(received[0],{url:'/admin/redemption/campaigns?page=3',method:'GET',token:'Bearer test-identity',body:{}});
    res=await fetch(base+'/campaigns',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer test-identity'},body:JSON.stringify({request_id:'same-operation',code:'TEST2026'})});
    assert.equal((await res.json()).data.request_id,'same-operation');
    assert.deepEqual(received[1].body,{request_id:'same-operation',code:'TEST2026'});
    res=await fetch(base+'/unavailable');assert.equal(res.status,503);assert.equal((await res.json()).retryable,true);
    res=await fetch(base+'/invalid');assert.equal(res.status,409);assert.equal((await res.json()).retryable,false);
    assert.equal((await fetch(base+'/campaigns',{method:'DELETE'})).status,405);
  } finally {
    config.calcServer.baseUrl=previous;
    await Promise.all([new Promise(r=>first.close(r)),new Promise(r=>second.close(r))]);
  }
});

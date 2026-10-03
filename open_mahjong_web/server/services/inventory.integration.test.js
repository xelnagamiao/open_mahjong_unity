const test = require('node:test');
const assert = require('node:assert/strict');
const express = require('express');
const config = require('../config/config');
const { requireAdmin } = require('../middleware/requireAdmin');
const { signToken } = require('../utils/jwt');

test('inventory HTTP proxy enforces admin access and forwards identity, body and errors', async () => {
  const calls = [];
  const upstream = express(); upstream.use(express.json());
  upstream.use((req,res) => {
    calls.push({path:req.path,body:req.body,token:req.headers.authorization});
    if (req.path.endsWith('/revoke')) return res.status(409).json({detail:'数量不足'});
    res.json({success:true,data:{items:[]}});
  });
  const source = upstream.listen(0,'127.0.0.1');
  await new Promise(resolve => source.once('listening',resolve));
  const original = config.calcServer.baseUrl;
  config.calcServer.baseUrl = `http://127.0.0.1:${source.address().port}`;
  const app = express(); app.use(express.json()); app.use('/inventory',requireAdmin,require('../routes/admin/inventory'));
  const server = app.listen(0,'127.0.0.1');
  await new Promise(resolve => server.once('listening',resolve));
  const base = `http://127.0.0.1:${server.address().port}/inventory`;
  try {
    assert.equal((await fetch(base+'/catalog')).status,401);
    const ordinary = signToken({user_id:700000101},config.admin.jwtSecret,60);
    assert.equal((await fetch(base+'/users/101/grant',{method:'POST',headers:{Authorization:`Bearer ${ordinary}`}})).status,403);
    assert.equal(calls.length,0);
    const token = signToken({user_id:[...config.admin.userIds][0]},config.admin.jwtSecret,60);
    const headers = {Authorization:`Bearer ${token}`,'Content-Type':'application/json'};
    assert.equal((await fetch(base+'/catalog',{headers})).status,200);
    const body = {item_id:3001,quantity:2,reason:'integration',request_id:'operation_1234'};
    assert.equal((await fetch(base+'/users/101/grant',{method:'POST',headers,body:JSON.stringify(body)})).status,200);
    assert.deepEqual(calls[1],{path:'/admin/inventory/users/101/grant',body,token:`Bearer ${token}`});
    const rejected = await fetch(base+'/users/101/revoke',{method:'POST',headers,body:JSON.stringify(body)});
    assert.equal(rejected.status,409);assert.equal((await rejected.json()).message,'数量不足');
    assert.equal((await fetch(base+'/catalog',{method:'DELETE',headers})).status,405);
  } finally {
    config.calcServer.baseUrl=original;
    await new Promise(resolve => server.close(resolve));
    await new Promise(resolve => source.close(resolve));
  }
});

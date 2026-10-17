import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

const fixtureRoot=new URL('../../../open_mahjong_unity/Assets/Tests/Fixtures/LocalHints/',import.meta.url)
async function load(name) {
  const result=await build({entryPoints:[fileURLToPath(new URL('../src/game2d/calc/'+name,import.meta.url))],bundle:true,write:false,platform:'node',format:'esm'})
  return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const regional=await load('regionalLocal.ts'),joker=await load('guobiao/jokerLocal.ts'),sixteen=await load('guobiao/sixteenLocal.ts')
function assertContains(actual,expected,label){
  if(Array.isArray(expected)){
    assert.equal(actual.length,expected.length,label)
    expected.forEach((value,index)=>assertContains(actual[index],value,label))
  }else if(expected&&typeof expected==='object'){
    for(const [key,value] of Object.entries(expected))assertContains(actual?.[key],value,label+' / '+key)
  }else assert.deepEqual(actual,expected,label)
}
const snapshots=JSON.parse(readFileSync(new URL('snapshot-parity.json',fixtureRoot),'utf8')).filter(row=>row.request.rule!=='hongkong')
test('complete discard previews match '+snapshots.length+' independent Python snapshots',()=>{
  snapshots.forEach((row,index)=>assertContains(regional.regionalSnapshot(row.request),row.expected,JSON.stringify({index,request:row.request})))
})
for(const name of ['regional','guobiao','sixteen']) {
  const cases=JSON.parse(readFileSync(new URL(name+'-parity.json',fixtureRoot),'utf8'))
  test(name+' local hints match '+cases.length+' independent Python cases',()=>{
    cases.forEach((row,index)=>{
      const before=[...row.hand];before.splice(before.indexOf(row.win),1)
      if(name==='regional'){
        assert.deepEqual(regional.regionalWaiting(row.rule,before,row.melds,row.context),row.waits,JSON.stringify({index,row}))
        const actual=regional.regionalDescribe(row.rule,row.hand,row.melds,row.win,row.context)
        for(const [key,value] of Object.entries(row.expected))assert.deepEqual(actual[key],value,JSON.stringify({index,key,row,actual}))
      }else{
        const actual=name==='sixteen'?sixteen.sixteenScore(row.hand,row.melds,row.way,row.win,row.allow):joker.jokerScore(row.hand,row.melds,row.way,row.win,row.sub,row.multi,row.bonus,row.progressive)
        const waits=name==='sixteen'?sixteen.sixteenWaiting(before,row.melds):joker.jokerWaiting(before,row.melds,row.sub)
        assert.equal(actual.score,row.score,JSON.stringify({index,row,actual}))
        assert.equal(actual.qualifying,Math.max(0,row.qualifying),JSON.stringify({index,row,actual}))
        assert.deepEqual(waits,row.waits,JSON.stringify({index,row}))
      }
    })
  })
}

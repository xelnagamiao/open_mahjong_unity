import test from 'node:test'
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import vm from 'node:vm'
import {localHintPlugin,pluginPath} from '../scripts/build-unity-local-hints.mjs'

test('Unity WebGL packages exactly the current shared worker',async()=>{
  assert.equal(readFileSync(pluginPath,'utf8'),await localHintPlugin())
})

test('packaged WebGL worker calculates without network and preserves response identities',()=>{
  let library
  vm.runInNewContext(readFileSync(pluginPath,'utf8'),{LibraryManager:{library:{}},mergeInto:(_,value)=>{library=value}})
  const replies=[],self={postMessage:message=>replies.push(message)}
  assert.equal(typeof library.$OMLocalHintWorkerSource,'function')
  vm.runInNewContext(library.$OMLocalHintWorkerSource(),{self,console,fetch:()=>assert.fail('No server computation')})
  self.onmessage({data:{id:10,kind:'guobiao.waits',body:{sub_rule:'guobiao/huahun',hand_tiles:[15,16,45,45],tiles_combination:['s12','s15','k44']}}})
  self.onmessage({data:{id:11,kind:'regional.snapshot',body:{rule:'hangzhou',hand:[11,12,13,21,22,23,31,32,33,41,41,41,45],melds:[],context:{}}}})
  self.onmessage({data:{id:12,kind:'guobiao.score',body:{sub_rule:'guobiao/four_jokers',hand_tiles:[25,25],tiles_combination:['s12','s24','s36','s17'],get_tile:25,ron_way:['点和','和单张'],self_way:['自摸','和单张'],hepai_limit:8}}})
  assert.deepEqual(JSON.parse(JSON.stringify(replies[0].result.waits)),[14,17,51,52,53,54])
  assert.deepEqual(JSON.parse(JSON.stringify(replies[1].result.waiting_tiles)),[45,46])
  assert.equal(replies[2].result.kind,'ron_only')
  assert.equal(replies[2].result.label,'仅点和')
  assert.deepEqual(replies.map(row=>row.id),[10,11,12])
})

test('Unity JavaScript bridge returns results to the requesting component and shuts down',()=>{
  const library={},sent=[],urls=new Map();let stopped=0,sequence=0
  const context={LibraryManager:{library},mergeInto:(target,value)=>Object.assign(target,value),console,
    UTF8ToString:value=>value,SendMessage:(receiver,method,json)=>sent.push({receiver,method,data:JSON.parse(json)}),
    Blob:class{constructor(parts){this.source=parts.join('')}},
    URL:{createObjectURL(blob){const key=String(++sequence);urls.set(key,blob.source);return key},revokeObjectURL(key){urls.delete(key)}},
    Worker:class{
      constructor(url){this.scope={self:{postMessage:data=>this.onmessage({data})},console};vm.runInNewContext(urls.get(url),this.scope)}
      postMessage(data){this.scope.self.onmessage({data})}
      terminate(){stopped++}
    }}
  vm.createContext(context)
  vm.runInContext(readFileSync(pluginPath,'utf8'),context)
  context.OMLocalHintWorkerSource=library.$OMLocalHintWorkerSource
  vm.runInContext(readFileSync(new URL('../../../open_mahjong_unity/Assets/Plugins/LocalHintsBridge.jslib',import.meta.url),'utf8'),context)
  context.OMLocalHints=library.$OMLocalHints
  library.OMLocalHintSubmit(JSON.stringify({id:42,kind:'guobiao.waits',body:{sub_rule:'guobiao/huahun',hand_tiles:[15,16,45,45],tiles_combination:['s12','s15','k44']}}),'LocalHintWebBridge')
  assert.equal(sent[0].receiver,'LocalHintWebBridge');assert.equal(sent[0].method,'OnLocalHint');assert.equal(sent[0].data.id,42)
  assert.deepEqual(sent[0].data.result.waits,[14,17,51,52,53,54])
  assert.equal(Object.keys(context.OMLocalHints.pending).length,0)
  assert.equal(urls.size,0)
  library.OMLocalHintShutdown();assert.equal(stopped,1)
})

test('packaged Shangrao worker evaluates self draw, unmet hands and discard previews offline',()=>{
  let library
  vm.runInNewContext(readFileSync(pluginPath,'utf8'),{LibraryManager:{library:{}},mergeInto:(_,value)=>{library=value}})
  const replies=[],self={postMessage:message=>replies.push(message)}
  vm.runInNewContext(library.$OMLocalHintWorkerSource(),{self,console,fetch:()=>assert.fail('No server computation')})
  const hand=[11,11,13,13,15,15,21,21,23,23,25,25,47]
  const request={rule:'shangrao',hand,melds:[],context:{indicator:45}}
  for(const [id,body] of [[61,request],[62,{...request,context:{indicator:45,blocked:true}}],[63,{...request,hand:[...hand,31]}]]){
    self.onmessage({data:{id,kind:'regional.snapshot',body}})
  }
  assert.deepEqual(replies.map(row=>row.id),[61,62,63])
  assert.equal(replies[0].result.waiting_tiles.length,34)
  assert.equal(replies[0].result.waiting_details[31].label,'仅自摸\n2分')
  assert.equal(replies[1].result.waiting_details[31].label,'未满足')
  assert.equal(replies[1].result.waiting_details[31].points,0)
  assert.equal(replies[2].result.waiting_details_by_discard[31][29].points,2)
})

test('packaged Shangrao worker respects V0.2 scores and keeps historical rule versions',()=>{
  let library
  vm.runInNewContext(readFileSync(pluginPath,'utf8'),{LibraryManager:{library:{}},mergeInto:(_,value)=>{library=value}})
  const replies=[],self={postMessage:message=>replies.push(message)}
  vm.runInNewContext(library.$OMLocalHintWorkerSource(),{self,console,fetch:()=>assert.fail('No server computation')})
  const samples=[
    {hand:[11,12,13,14,15,16,17,18,19,12,13,14,16],indicator:45,config:{qingyise:true},win:16,points:[8,4]},
    {hand:[19,19,21,21,29,29,31,31,39,39,41,41,12],indicator:11,config:{allow_special_bao:true},win:45,points:[4,2]},
    {hand:[11,14,17,21,24,27,31,34,41,42,43,44,12],indicator:11,config:{allow_special_bao:true},win:45,points:[0,2]},
  ]
  for(const sample of samples){
    for(const [i,version] of ['shangrao-v0.2','shangrao-v1'].entries()){
      self.onmessage({data:{id:replies.length+1,kind:'regional.snapshot',body:{rule:'shangrao',hand:sample.hand,melds:[],context:{indicator:sample.indicator,config:{...sample.config,rule_version:version}}}}})
      const detail=replies.at(-1).result.waiting_details[sample.win]
      assert.equal(detail?.points??0,sample.points[i],`${version} ${sample.win}`)
      if(sample.points[i])assert.equal(detail.label,`仅自摸\n${sample.points[i]}分`)
      else assert.equal(replies.at(-1).result.waiting_tiles.includes(sample.win),false)
    }
  }
})

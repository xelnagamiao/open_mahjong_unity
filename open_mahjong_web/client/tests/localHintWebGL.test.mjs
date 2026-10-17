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
  assert.deepEqual(JSON.parse(JSON.stringify(replies[0].result.waits)),[14,17,51,52,53,54])
  assert.deepEqual(JSON.parse(JSON.stringify(replies[1].result.waiting_tiles)),[45,46])
  assert.deepEqual(replies.map(row=>row.id),[10,11])
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

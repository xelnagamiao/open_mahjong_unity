// One worker shares a bounded queue across all local hint consumers.
let worker:Worker|null=null,nextId=0
const pending=new Map<number,{resolve:(value:any)=>void;reject:(error:Error)=>void}>()
export function localHintJob<T>(kind:string,body:unknown):Promise<T>{
  if(pending.size>=128)return Promise.reject(new Error('Local hint queue is full'))
  // Unit tests and environments without workers retain a local, asynchronous path.
  if(typeof Worker==='undefined')return new Promise<T>((resolve,reject)=>{
    setTimeout(()=>{void import('./localHintEngine').then(m=>resolve(m.calculateLocalHint(kind,body) as T)).catch(reject)},0)
  })
  if(!worker){
    worker=new Worker(new URL('./localHintWorker.ts',import.meta.url),{type:'module'})
    worker.onmessage=({data})=>{const job=pending.get(data.id);if(!job)return;pending.delete(data.id);data.error?job.reject(new Error(data.error)):job.resolve(data.result)}
    worker.onerror=(event)=>{for(const job of pending.values())job.reject(new Error(event.message));pending.clear();worker?.terminate();worker=null}
  }
  const id=++nextId
  return new Promise<T>((resolve,reject)=>{pending.set(id,{resolve,reject});worker!.postMessage({id,kind,body})})
}

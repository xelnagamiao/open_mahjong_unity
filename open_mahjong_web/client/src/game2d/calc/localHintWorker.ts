import {calculateLocalHint} from './localHintEngine'
self.onmessage=({data})=>{
  try{self.postMessage({id:data.id,result:calculateLocalHint(data.kind,data.body)})}
  catch(error){self.postMessage({id:data.id,error:error instanceof Error?error.message:String(error)})}
}

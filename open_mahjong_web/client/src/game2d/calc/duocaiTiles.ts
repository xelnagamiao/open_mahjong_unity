import catalog from '../../../public/rulebooks/duocai/catalog.json'
import {TILES, ORPHANS} from './localJokerHand'

export {TILES, ORPHANS}
export const JOKERS = catalog.map(row => row.id)
const bonusFaces = TILES.filter(t => t < 40 && [3,7].includes(t % 10))
const redFaces = [41,42,43,44,46]
export const PHYSICAL = [...TILES, ...bonusFaces.map(t=>1000+t), ...bonusFaces.map(t=>2000+t), ...redFaces.map(t=>3000+t), ...JOKERS]
export const DOMAIN = new Map(PHYSICAL.map(t=>[t, catalog.find(row=>row.id===t)?.domain ?? [t%1000]]))
export const SUPPLY = new Map(PHYSICAL.map(t=>[t, t>=1000?1:bonusFaces.includes(t)?2:redFaces.includes(t)?3:4]))
export const joker = (t:number) => t>=5000 && t<5040 && Number.isInteger(t)
export const face = (t:number) => joker(t)?0:t%1000
export const bonus = (t:number) => t>=2000&&t<3000?-1:t>=1000&&t<4000?1:0
export function validPhysical(tiles:number[]):boolean {
  if(tiles.filter(joker).length>8)return false
  const counts = new Map<number,number>()
  for(const t of tiles){const n=(counts.get(t)??0)+1;counts.set(t,n);if(!Number.isInteger(t)||!SUPPLY.has(t)||n>SUPPLY.get(t)!)return false}
  return true
}
export function allows(tile:number, logical:number, kind:string, position=0, start=0, external=false, declared=''):boolean {
  if(!DOMAIN.get(tile)?.includes(logical))return false
  if(!joker(tile))return true
  if([5036,5037,5038].includes(tile))return !external
  if(tile===5030)return external&&kind==='sequence'
  if(tile===5031)return external&&(kind==='triplet'||kind==='kong'&&declared==='peng')
  if(tile===5032)return ['pair','triplet','kong'].includes(kind)
  if(tile===5033)return kind==='sequence'&&(position===0&&start%10<=6||position===2&&start%10>=2)
  if(tile===5034)return kind==='sequence'&&(position===1||position===2&&start%10===1||position===0&&start%10===7)
  if(tile===5035)return kind==='sequence'&&[1,7].includes(start%10)
  return true
}
export function dora(indicator:number):number {
  if(joker(indicator))return 0
  const value=face(indicator)
  if(value<40)return Math.floor(value/10)*10+value%10%9+1
  const cycle=value<45?[41,42,43,44]:[45,47,46]
  return cycle[(cycle.indexOf(value)+1)%cycle.length]
}

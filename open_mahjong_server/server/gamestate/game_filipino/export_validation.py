"""Regenerate deterministic C#/Python replay fixtures from the authoritative domain."""
import argparse,json
from pathlib import Path
from .test_roundtrip import export

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);parser.add_argument('--install-fixture',action='store_true')
    args=parser.parse_args();export(args.output)
    from .test_flow import state,act
    from .state_machine import Phase as P
    s=state(3);s.room_id=987651;s.initialize_round();s.start_game_recording();s.start_round_recording();s.open_action_window(s.opening_window())
    shots=[]
    for step in range(500):
        if s.machine.phase==P.END:break
        act(s)
        if s.opening_queue is None and (not shots or step==35 or step==75):
            shots.append(dict(name=f'table-{len(shots)}',packet=s.build_game_start_payload(0)))
    (Path(args.output)/'table-smoke.json').write_text(json.dumps(shots,ensure_ascii=False,default=str),encoding='utf-8')
    if args.install_fixture:
        root=Path(__file__).resolve().parents[4]
        data=json.loads((Path(args.output)/'filipino-0.json').read_text(encoding='utf-8'))
        path=root/'open_mahjong_unity/Assets/Tests/Fixtures/FilipinoReplay.json';path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
        path=root/'open_mahjong_server/server/gamestate/public/game_record_example_filipino.jsonc'
        path.write_text(json.dumps(data['record'],ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()

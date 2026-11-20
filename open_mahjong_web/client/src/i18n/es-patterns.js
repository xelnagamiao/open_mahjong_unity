// Capture groups only. Opaque names, IDs, file names and user text stay verbatim.
// translate is used only for semantic UI fields: tiles, jugadas, rules and parser errors.
export function createSpanishPatterns(translate) {
  const plural = (n, one, other) => Math.abs(Number(n)) === 1 ? one : other;
  const q = (n, one, other) => n + ' ' + plural(n, one, other);
  const pts = n => q(n, 'punto', 'puntos');
  const tiles = n => q(n, 'ficha', 'fichas');
  const remaining = n => plural(n, 'Queda ', 'Quedan ') + tiles(n);
  const seconds = n => q(n, 'segundo', 'segundos');
  const winds = { 东: 'Este', 南: 'Sur', 西: 'Oeste', 北: 'Norte' };
  const suits = { 万: 'caracteres', 饼: 'círculos', 筒: 'círculos', 条: 'bambúes' };
  const numerals = { 初: 1, 一: 1, 二: 2, 三: 3, 四: 4, 五: 5, 六: 6, 七: 7, 八: 8, 九: 9 };
  const pool = name => name === '立直' ? 'Riichi' : 'MCR + Riichi';
  const tile = value => {
    const numbered = value.match(/^([1-9])([万饼筒条])$/);
    if (numbered) return numbered[1] + ' de ' + suits[numbered[2]];
    const honors = { 东: 'Este', 南: 'Sur', 西: 'Oeste', 北: 'Norte', 中: 'Dragón rojo', 红中: 'Dragón rojo', 发: 'Dragón verde', 發: 'Dragón verde', 白: 'Dragón blanco', 白板: 'Dragón blanco' };
    if (Object.hasOwn(honors, value)) return honors[value];
    const ghostFlowers = { '梅（鬼）': 'Ciruelo (comodín)', '兰（鬼）': 'Orquídea (comodín)', '竹（鬼）': 'Bambú (comodín)', '菊（鬼）': 'Crisantemo (comodín)' };
    if (Object.hasOwn(ghostFlowers, value)) return ghostFlowers[value];
    return translate(value);
  };
  const tileSyntax = /^(?:[1-9][万饼筒条]|[东南西北中发發白]|红中|白板|[梅兰竹菊]（鬼）|[A-G][XY][1-9])$/;
  const tileList = value => {
    if (value === '无剩余牌') return translate(value);
    const parts = value.split('、');
    return parts.every(part => tileSyntax.test(part)) ? parts.map(tile).join(', ') : value;
  };
  const substitutions = value => {
    const parts = value.split('、').map(part => part.split(/代|→/));
    return parts.every(pair => pair.length === 2 && pair.every(part => tileSyntax.test(part)))
      ? parts.map(pair => tile(pair[0]) + ' → ' + tile(pair[1])).join(', ') : value;
  };
  return [
    // Only fixed replay diagnostics are translated here; arbitrary names/errors stay opaque.
    { match: /^(无法打开比赛观战|恢复观战失败|比赛观战已中断)[：:]\s*(Missing match identity|Unsupported match record version|Missing tables|Invalid table|Selected table unavailable|Invalid table permission set|Invalid sequence|Unknown match event|Round already exists|Unbound round|Action prefix mismatch|Unknown table event|Table identity mismatch|Round metadata mismatch|Action metadata mismatch|Final table streams have not arrived)$/, replace: (prefix, reason) => translate(prefix) + ': ' + translate(reason) },
    { match: /^确定将用户名改为「(.+)」？改名成功时将消耗 1 张改名卡，并追溯更新该账号全部历史牌谱中的名字。此操作不可撤销。$/, replace: name => '¿Cambiar el nombre de usuario a «' + name + '»? Si se completa, se consumirá 1 tarjeta de cambio de nombre y se actualizará el nombre en todos los registros históricos de esta cuenta. Esta acción es irreversible.' },
    { match: /^手牌 (\d+)\s*\/\s*(\d+)$/, replace: (n, max) => 'Mano ' + n + '/' + max },
    { match: /^手牌 (\d+) 张$/, replace: n => 'Mano: ' + tiles(n) },
    { match: /^副露（(\d+)\/4）$/, replace: n => 'Combinaciones expuestas (' + n + '/4)' },
    { match: /^番种构成（(\d+) 项）$/, replace: n => 'Composición de jugadas (' + q(n, 'elemento', 'elementos') + ')' },
    { match: /^牌型成立；花牌不计入 8 番起和条件（不计花牌 ([\d.]+) 番）。$/, replace: n => 'La forma de la mano es válida; las flores no cuentan para el mínimo de 8 puntos (' + pts(n) + ' sin flores).' },
    { match: /^副露牌组（(\d+)\/4，点击方块后从牌面选牌）$/, replace: n => 'Grupos expuestos (' + n + '/4; pulsa el recuadro y elige fichas)' },
    { match: /^还可选 (\d+) 张（(\d+) 张成组）$/, replace: (n, size) => 'Aún puedes elegir ' + tiles(n) + ' (' + tiles(size) + ' por grupo)' },
    { match: /^牌面（(.+)）$/, replace: label => 'Fichas (' + translate(label) + ')' },
    { match: /^进张 (\d+) 张 · (\d+) 种$/, replace: (n, kinds) => 'Mejora: ' + tiles(n) + ' · ' + q(kinds, 'tipo', 'tipos') },
    { match: /^输入副露 #(\d+)$/, replace: n => 'Introducir combinación expuesta #' + n },
    { match: /^副露 #(\d+)( 输入位置)?$/, replace: (n, input) => 'Combinación expuesta #' + n + (input ? ' · Posición de entrada' : '') },
    { match: /^(.+?)(?: ×(\d+))?（(\d+) 番）$/, replace: (name, count, fan) => translate(name) + (count ? ' ×' + count : '') + ' (' + pts(fan) + ')' },
    { match: new RegExp('^(' + spanishMcrPatternNames.join('|') + ') ×(\\d+)$'), replace: (name, count) => translate(name) + ' ×' + count },
    { match: /^共 (\d+) 种拆解$/, replace: n => q(n, 'descomposición', 'descomposiciones') + ' en total' },
    { match: /^共 (\d+) 种（按分数从高到低）$/, replace: n => q(n, 'descomposición', 'descomposiciones') + ' en total (de mayor a menor puntuación)' },
    { match: /^(\d+)\/(\d+) 张（手牌 (\d+) \+ 副露 (\d+)）$/, replace: (n, max, hand, melds) => n + '/' + max + ' fichas (' + hand + ' en mano + ' + melds + ' expuestas)' },
    { match: /^添加到副露 #(\d+)，仅高亮牌可组成合法牌组$/, replace: n => 'Añadir a la combinación expuesta #' + n + '; solo las fichas resaltadas forman un grupo válido' },
    { match: /^手牌须为 (\d+) 或 (\d+) 张（副露每组按 3 张计算），当前 (\d+) 张$/, replace: (a, b, current) => 'La mano debe tener ' + a + ' o ' + b + ' fichas (cada combinación expuesta cuenta como 3); ahora tiene ' + current },
    { match: /^手牌已达上限 (\d+) 张$/, replace: n => 'La mano ha alcanzado el límite de ' + tiles(n) },
    { match: /^合计已达 (\d+) 张上限$/, replace: n => 'Se ha alcanzado el límite total de ' + tiles(n) },
    { match: /^牌数最少 12 张（当前 (\d+) 张）$/, replace: n => 'Se necesitan al menos 12 fichas (actualmente ' + n + ')' },
    { match: /^副露 #(\d+) 需至少 3 张，当前 (\d+) 张$/, replace: (id, n) => 'La combinación expuesta #' + id + ' necesita al menos 3 fichas; actualmente ' + n },
    { match: /^牌 (.+) 已达 4 张上限$/, replace: name => 'Se ha alcanzado el límite de 4 ejemplares de ' + tile(name) },
    { match: /^牌 (.+) 超过 4 张$/, replace: name => 'Hay más de 4 ejemplares de ' + tile(name) },
    { match: /^(.+)超过 4 张，请检查手牌、副露与已见牌$/, replace: name => 'Hay más de 4 ejemplares de ' + tile(name) + '. Comprueba la mano, las combinaciones expuestas y las fichas vistas.' },
    { match: /^牌 (.+) 已在手牌或副露中$/, replace: name => tile(name) + ' ya está en la mano o en una combinación expuesta' },
    { match: /^牌 (.+) 不能与当前副露组成合法牌组$/, replace: name => tile(name) + ' no forma un grupo válido con la combinación expuesta actual' },
    { match: /^非法牌码：(.+)（应为 AX1～GY9 格式）$/, replace: code => 'Código de ficha no válido: ' + code + ' (se requiere el formato AX1～GY9)' },
    { match: /^虹雀牌不可重复[:：] (.+)$/, replace: name => 'Las fichas de Hongque no pueden repetirse: ' + tile(name) },
    { match: /^非法虹雀牌码: (.+)$/, replace: code => 'Código de ficha Hongque no válido: ' + code },
    { match: /^(简写解析失败|计算失败)[:：]\s*(.+)$/, replace: (kind, detail) => (kind === '简写解析失败' ? 'Error al interpretar la notación' : 'Error de cálculo') + ': ' + translate(detail) },
    { match: /^副露格式不正确：(.+)$/, replace: detail => 'Formato de combinación expuesta incorrecto: ' + detail },
    { match: /^不支持的牌号：(.+)$/, replace: code => 'Número de ficha no admitido: ' + code },
    { match: /^非法牌号: (.+)$/, replace: code => 'Número de ficha no válido: ' + code },
    { match: /^非法副露格式: (.+)$/, replace: detail => 'Formato de combinación expuesta no válido: ' + detail },
    { match: /^非法副露牌号: (.+)$/, replace: code => 'Número de ficha expuesta no válido: ' + code },
    { match: /^非法顺子格式: (.+)$/, replace: detail => 'Formato de chow no válido: ' + detail },
    { match: /^「(.+)」前缺少数字$/, replace: symbol => 'Falta un número antes de «' + symbol + '»' },
    { match: /^字牌仅允许 1-7：(.+)$/, replace: detail => 'Los honores solo admiten 1-7: ' + detail },
    { match: /^非法字符：(.+)$/, replace: symbol => 'Carácter no válido: ' + symbol },
    { match: /^(\d+)花$/, replace: n => q(n, 'flor', 'flores') },
    { match: /^花牌 (\d+)$/, replace: n => 'Flores: ' + n },
    { match: /^顺子 (\d+)-(\d+)-(\d+)$/, replace: (a, b, c) => 'Chow ' + a + '-' + b + '-' + c },
    { match: /^暗顺 (.+)（中心）$/, replace: name => 'Chow oculto ' + tile(name) + ' (centro)' },
    { match: /^(明刻|暗刻|明杠|暗杠|雀头) (.+)$/, replace: (kind, name) => translate(kind) + ' ' + tile(name) },
    { match: /^切(.+)，摸(.+)$/, replace: (discard, draw) => 'Descartar ' + tile(discard) + ', robar ' + tile(draw) },
    { match: /^摸([1-9][万饼筒条]|[东南西北中白发]|[A-G][XY][1-9])$/, replace: name => 'Robar ' + tile(name) },
    { match: /^切([1-9][万饼筒条]|[东南西北中白发]|[A-G][XY][1-9])$/, replace: name => 'Descartar ' + tile(name) },
    { match: /^(\d+) 向听$/, replace: n => n + ' shanten' },
    { match: /^牌谱 (\d+)$/, replace: n => q(n, 'registro', 'registros') },
    { match: /^比赛场 · (.+)$/, replace: name => 'Torneo · ' + name },
    { match: /^进入匹配（队列 (\d+)）$/, replace: n => 'Buscar partida (cola: ' + n + ')' },
    { match: /^进入匹配（(国标\+立直|立直) · 队列 (\d+)）$/, replace: (name, n) => 'Buscar partida (' + pool(name) + ' · Cola: ' + n + ')' },
    { match: /^匹配队列（(\d+) 人）$/, replace: n => 'Cola de emparejamiento (' + q(n, 'jugador', 'jugadores') + ')' },
    { match: /^匹配中（(\d+)）…$/, replace: n => 'Buscando partida (' + n + ')…' },
    { match: /^匹配中（(国标\+立直|立直) · (\d+)）…$/, replace: (name, n) => 'Buscando partida (' + pool(name) + ' · ' + n + ')…' },
    { match: /^(国标\+立直|立直) · 匹配队列（(\d+) 人）$/, replace: (name, n) => pool(name) + ' · Cola de emparejamiento (' + q(n, 'jugador', 'jugadores') + ')' },
    { match: /^(国标\+立直|立直) · 你正在匹配（队列 (\d+) 人）$/, replace: (name, n) => pool(name) + ' · Buscando partida (cola: ' + q(n, 'jugador', 'jugadores') + ')' },
    { match: /^(\d+)胜 (\d+)负 · 胜率 ([\d.]+)%$/, replace: (wins, losses, rate) => q(wins, 'victoria', 'victorias') + ', ' + q(losses, 'derrota', 'derrotas') + ' · Tasa de victoria ' + rate + '%' },
    { match: /^(\d+) 连胜$/, replace: n => q(n, 'victoria consecutiva', 'victorias consecutivas') },
    { match: /^(\d+)级$/, replace: n => n + ' kyu' },
    { match: /^(\d+)段$/, replace: n => n + ' dan' },
    { match: /^([初二三四五六七八九])段$/, replace: n => numerals[n] + ' dan' },
    { match: /^番种统计（(\d+)）$/, replace: n => 'Estadísticas de jugadas (' + n + ')' },
    { match: /^番种出现次数（(\d+)）$/, replace: n => 'Frecuencia de jugadas (' + n + ')' },
    { match: /^共 (\d+) 局$/, replace: n => q(n, 'mano', 'manos') + ' en total' },
    { match: /^(\d+)\s*局$/, replace: n => q(n, 'mano', 'manos') },
    { match: /^节点 (\d+)$/, replace: n => 'Nodo ' + n },
    { match: /^([一二三四])位（(\d+)）$/, replace: (rank, n) => numerals[rank] + '.º puesto (' + n + ')' },
    { match: /^以 (.+) 连接$/, replace: name => 'Conectar como ' + name },
    { match: /^座位 (\d+)( · 房主)?$/, replace: (n, host) => 'Asiento ' + n + (host ? ' · Anfitrión' : '') },
    { match: /^房间 (.+)$/, replace: id => 'Sala ' + id },
    { match: /^玩家 (.+)$/, replace: name => 'Jugador ' + name },
    { match: /^第 (\d+) 名$/, replace: n => 'Puesto ' + n },
    { match: /^第(\d+)局$/, replace: n => 'Mano ' + n },
    { match: /^(\d+)巡$/, replace: n => 'Turno ' + n },
    { match: /^剩余 (\d+) 张$/, replace: remaining },
    { match: /^(\d+) 番起和$/, replace: n => 'Mínimo de ' + pts(n) + ' para ganar' },
    { match: /^匹配成功，(\d+) 秒后进入游戏$/, replace: n => 'Partida encontrada. Entrando en ' + seconds(n) },
    { match: /^局时 (\d+) 秒$/, replace: n => 'Reserva por mano: ' + seconds(n) },
    { match: /^步时 (\d+) 秒$/, replace: n => 'Tiempo por acción: ' + seconds(n) },
    { match: /^已保存图片：(.+)$/, replace: name => 'Imagen guardada: ' + name },
    { match: /^使用或拖动第 (\d+) 个牌背$/, replace: n => 'Usar o arrastrar el reverso ' + n },
    { match: /^修改第 (\d+) 个牌背颜色$/, replace: n => 'Cambiar el color del reverso ' + n },
    { match: /^删除第 (\d+) 个牌背$/, replace: n => 'Eliminar el reverso ' + n },
    { match: /^延迟：(.+)$/, replace: value => 'Latencia: ' + translate(value) },
    { match: /^确定\((\d+)\)$/, replace: n => 'Confirmar (' + n + ')' },
    { match: /^(\d+) 秒后重发$/, replace: n => 'Reenviar en ' + seconds(n) },
    { match: /^(\d+(?:\.\d+)?)\s*番$/, replace: pts },
    { match: /^(\d+(?:\.\d+)?)\s*[分点]$/, replace: pts },
    { match: /^(\d+)\s*秒$/, replace: seconds },
    { match: /^(\d+) 圈$/, replace: n => q(n, 'ronda', 'rondas') },
    { match: /^复式 (\d+) 局$/, replace: n => 'Duplicado · ' + q(n, 'mano', 'manos') },
    { match: /^第 (\d+) 至 (\d+) 张$/, replace: (first, last) => 'Fichas ' + first + ' a ' + last },
    { match: /^第 (\d+) 张(，已摸)?$/, replace: (n, drawn) => 'Ficha ' + n + (drawn ? ', ya robada' : '') },
    { match: /^加载更多（(\d+)\/(\d+)）$/, replace: (n, total) => 'Cargar más (' + n + '/' + total + ')' },
    { match: /^(\d+) 位$/, replace: n => n + '.º puesto' },
    { match: /^([东南西北])家初始手牌$/, replace: wind => 'Mano inicial de ' + winds[wind] },
    { match: /^第 (\d+) 局分数变动$/, replace: n => 'Cambio de puntos de la mano ' + n },
    { match: /^2D 资源加载失败：(.+)$/, replace: detail => 'Error al cargar recursos 2D: ' + translate(detail) },
    { match: /^加入房间 #(.+)$/, replace: id => 'Unirse a la sala #' + id },
    { match: /^(.+)的房间$/, replace: name => 'Sala de ' + name },
    { match: /^复制失败，请手动复制：(.+)$/, replace: value => 'Error al copiar. Copia manualmente: ' + value },
    { match: /^当前位置（第(\d+)局 node (\d+)）$/, replace: (hand, node) => 'Posición actual (mano ' + hand + ', node ' + node + ')' },
    { match: /^(\d+) 号玩家固定牌山$/, replace: n => 'Muro fijo del jugador ' + n },
    { match: /^-([\d.]+) × 均失 × ([\d.]+)$/, replace: (a, b) => '-' + a + ' × pérdida media × ' + b },
    { match: /^(投票结束对局|投票暂停对局|投票解除暂停|玩家同意结束对局|即将结束对局) (.*)$/, replace: (kind, detail) => translate(kind) + ' ' + detail },
    { match: /^余 (\d+) 枚$/, replace: remaining },
    { match: /^余 (\d+)\/(\d+)$/, replace: (left, total) => 'Quedan ' + left + '/' + total },
    { match: /^等待中:\s+(\d+)\/4$/, replace: n => 'En espera: ' + n + '/4' },
    { match: /^(.+) 自摸和$/, replace: name => name + ' cierra del muro' },
    { match: /^(.+) 和,\s+(.+) 铳$/, replace: (winner, discarder) => winner + ' gana; ' + discarder + ' da el descarte ganador' },
    { match: /^共 ([\d.]+) 番 \(各 ([\d.-]+')\)$/, replace: (fan, loss) => 'Total: ' + pts(fan) + ' (' + loss + ' cada uno)' },
    { match: /^共 ([\d.]+) 番 \(([\d.-]+')\)$/, replace: (fan, loss) => 'Total: ' + pts(fan) + ' (' + loss + ')' },
    { match: /^音量：(\d+)%$/, replace: n => 'Volumen: ' + n + '%' },
    { match: /^本局完整初始牌墙（(\d+) 张）$/, replace: n => 'Muro inicial completo de esta mano (' + tiles(n) + ')' },
    { match: /^(\d+) 号 · ([东南西北])位 · 前 (\d+) 张为起始手牌$/, replace: (n, wind, hand) => 'N.º ' + n + ' · ' + winds[wind] + ' · Las primeras ' + tiles(hand) + ' forman la mano inicial' },
    { match: /^用户\s*(\d+)$/, replace: id => 'Usuario ' + id },
    { match: /^座位\s*(\d+)$/, replace: n => 'Asiento ' + n },
    { match: /^(\d+)\s*张$/, replace: tiles },
    { match: /^(\d+)倍$/, replace: n => '×' + n },
    { match: /^([1-9])([万饼筒条])$/, replace: (n, suit) => n + ' de ' + suits[suit] },
    { match: /^白板固定代(.+?)(；牌面保留实体身份。)?$/, replace: (name, suffix) => 'El dragón blanco representa siempre ' + tile(name) + (suffix ? '; las imágenes conservan la identidad física de las fichas.' : '') },
    { match: /^财神 (.+) · 连庄(\d+)次$/, replace: (name, n) => 'Comodín Caishen: ' + tile(name) + ' · ' + q(n, 'repetición del repartidor', 'repeticiones del repartidor') },
    { match: /^([东南西北])起：财神(\d+)张，财分([\d.-]+)，本局([\d.-]+)分$/, replace: (wind, n, score, net) => winds[wind] + ' inicial: ' + q(n, 'Caishen', 'Caishen') + ', puntos Caishen ' + score + ', saldo de la mano ' + pts(net) },
    { match: /^翻开 (.+)，和牌鸡为 (.+)$/, replace: (indicator, chicken) => 'Se revela ' + tile(indicator) + '; el gallo de victoria es ' + tile(chicken) },
    { match: /^(.+) 付 (.+) ([\d.-]+)分：(.+)$/, replace: (payer, payee, points, reason) => payer + ' paga a ' + payee + ' ' + pts(points) + ': ' + translate(reason) },
    { match: /^白板财神 · (\d+)老庄 · (\d+)倍$/, replace: (n, factor) => 'Dragón blanco como Caishen · ' + q(n, 'repetición del repartidor', 'repeticiones del repartidor') + ' · ×' + factor },
    { match: /^杠后暗弃(\d+)张$/, replace: n => tiles(n) + plural(n, ' descartada', ' descartadas') + ' boca abajo tras kong' },
    { match: /^和牌张按 (.+) 计分$/, replace: name => 'La ficha ganadora se puntúa como ' + tile(name) },
    { match: /^扎鸟：(.+) · 中 (\d+) 鸟（每家另付 (\d+) 分）$/, replace: (names, hits, points) => 'Pájaros: ' + tileList(names) + ' · ' + q(hits, 'acierto', 'aciertos') + ' (cada jugador paga además ' + pts(points) + ')' },
    { match: /^计分牌型：(.+)$/, replace: shape => 'Forma de mano puntuada: ' + tileList(shape) },
    { match: /^红中替代：(.+)$/, replace: detail => 'Sustituciones del dragón rojo: ' + substitutions(detail) },
    { match: /^长春 · (无宝|宝：.+)$/, replace: value => 'Changchun · ' + (value === '无宝' ? 'Sin bao' : 'Bao: ' + tile(value.slice(2))) },
    { match: /^座位(\d+)末张单放：(.+)$/, replace: (n, name) => 'Asiento ' + n + ', última ficha descartada sola: ' + tile(name) },
    { match: /^座位(\d+)应付 ([\d.-]+)分（(\d+)番）$/, replace: (n, points, fan) => 'El asiento ' + n + ' debe pagar ' + pts(points) + ' (' + fan + ' fan)' },
    { match: /^，由座位(\d+)包庄$/, replace: n => '; lo asume el asiento ' + n },
    { match: /^奖马顺序：(.+)$/, replace: names => 'Orden de caballos de bonificación: ' + tileList(names) },
    { match: /^中马 (\d+)张$/, replace: n => q(n, 'caballo acertado', 'caballos acertados') },
    { match: /^；责任支付：(.+)$/, replace: name => '; pago de responsabilidad: ' + name },
    { match: /^(\d+)番；基本分 ([\d.-]+)分；系数 ×([\d.]+)$/, replace: (fan, points, factor) => fan + ' fan; base: ' + pts(points) + '; factor ×' + factor },
    { match: /^鬼牌解释：(.+)$/, replace: detail => 'Interpretación de comodines: ' + substitutions(detail) },
    { match: /^(明杠|暗杠|加杠|和牌|财神)：东([+-]?\d+) 南([+-]?\d+) 西([+-]?\d+) 北([+-]?\d+)$/, replace: (kind, east, south, west, north) => translate(kind) + ': Este ' + east + ' / Sur ' + south + ' / Oeste ' + west + ' / Norte ' + north },
    { match: /^(\d+)庄$/, replace: n => q(n, 'turno de repartidor', 'turnos de repartidor') },
    { match: /^连庄上限掷骰：(.+)；下庄为本局([东南西北])家。$/, replace: (dice, wind) => 'Dados al alcanzar el límite de repeticiones: ' + dice.replaceAll('、', ', ') + '; el próximo repartidor es el actual ' + winds[wind] + '.' },
    { match: /^点 · (\d+) 番 (\d+) 符$/, replace: (han, fu) => 'puntos · ' + han + ' han ' + fu + ' fu' },
    { match: /^· 底分 (\d+)$/, replace: n => '· Base ' + n },
    { match: /^· 副露 (\d+) 组$/, replace: n => '· ' + q(n, 'combinación expuesta', 'combinaciones expuestas') },
    { match: /^已见 (\d+) 张 · 推演舍牌 (\d+) 张$/, replace: (seen, discarded) => 'Vistas: ' + tiles(seen) + ' · Descartes simulados: ' + tiles(discarded) },
    { match: /^(\d+) 张等效手牌$/, replace: n => q(n, 'ficha equivalente en mano', 'fichas equivalentes en mano') },
    { match: /^再输入 (\d+) 张$/, replace: n => 'Introduce ' + tiles(n) + ' más' },
    { match: /^(\d+) 种 · 进张 (\d+) 张$/, replace: (kinds, n) => q(kinds, 'tipo', 'tipos') + ' · ' + q(n, 'ficha de mejora', 'fichas de mejora') },
    { match: /^(\d+) 种$/, replace: n => q(n, 'tipo', 'tipos') },
    { match: /^([\d.]+) 番 · 不足起和$/, replace: n => pts(n) + ' · Mínimo no alcanzado' },
    { match: /^([\d.]+) 番 · ([\d.]+) 底分$/, replace: (fan, base) => pts(fan) + ' · Base ' + base },
    { match: /^(.+?) ([\d.]+)番$/, replace: (name, n) => translate(name) + ' ' + pts(n) },
    { match: /^(\d+)\. (初始手牌|输入手牌|录入牌|移除手牌|切牌|点和试算|摸牌|切换规则|调整和牌牌型|清空手牌|指定和牌张|退回摸牌前|调整已见牌|清空推演牌河|调整和牌条件|调整副露|载入分享手牌|示例)$/, replace: (n, label) => n + '. ' + translate(label) },
    { match: /^待审核：拟改为「(.+)」$/, replace: name => 'Pendiente de revisión: cambiar a «' + name + '»' },
    { match: /^(\d{4}-\d{2}-\d{2}) 起$/, replace: date => 'Desde ' + date },
    { match: /^至 (\d{4}-\d{2}-\d{2})$/, replace: date => 'Hasta ' + date },
    { match: /^匹配到 (\d+) 名玩家，已显示「(.+)」$/, replace: (n, name) => q(n, 'jugador encontrado', 'jugadores encontrados') + '; se muestra «' + name + '»' },
    { match: /^确认删除公告「(.+)」？$/, replace: title => '¿Eliminar el anuncio «' + title + '»?' },
    { match: /^确认删除房间 (.+)？$/, replace: id => '¿Eliminar la sala ' + id + '?' },
    { match: /^确认开启赛事「(.+)」？$/, replace: name => '¿Abrir el torneo «' + name + '»?' },
    { match: /^确认关闭赛事「(.+)」？关闭后该赛事将被封存，再次开启需平台管理员进行审核$/, replace: name => '¿Cerrar el torneo «' + name + '»? Se archivará y su reapertura necesitará revisión de un administrador de la plataforma.' },
    { match: /^确认申请重新开启赛事「(.+)」？$/, replace: name => '¿Solicitar la reapertura del torneo «' + name + '»?' },
    { match: /^确认移除赛事子管理员「(.+)」？$/, replace: name => '¿Eliminar al administrador auxiliar del torneo «' + name + '»?' },
    { match: /^持有 (\d+) 张$/, replace: n => q(n, 'tarjeta disponible', 'tarjetas disponibles') },
    { match: /^（可用 (\d+) 张）$/, replace: n => '(' + q(n, 'utilizable', 'utilizables') + ')' },
    { match: /^添加到手牌（合计上限 14 张）$/, replace: () => 'Añadir a la mano (máximo de 14 fichas en total)' },
    { match: /^座位(\d+) (明杠|暗杠|加杠|特殊杠|特殊加杠|杠)：([+\-\d.]+ \/ [+\-\d.]+ \/ [+\-\d.]+ \/ [+\-\d.]+)$/, replace: (seat, kind, scores) => 'Asiento ' + seat + ' · ' + translate(kind) + ': ' + scores },
    { match: /^(初级场|中级场|高级场|MCRPL|天梯|国标|国标麻将|立直|立直麻将|青雀|四川|长沙|古典|虹雀|香港|台湾|三人立直|透明国标) · (天梯对局|自定义房间对局|比赛对局|本地牌谱)$/, replace: (rule, kind) => translate(rule) + ' · ' + translate(kind) },
    { match: /^白板为财神；牌面保留实体身份。$/, replace: () => 'El dragón blanco es el comodín Caishen; las imágenes conservan la identidad física de las fichas.' },
    { match: /^对局奖励 · (\d+) 小局$/, replace: n => 'Recompensa de partida · ' + q(n, 'mano', 'manos') },
    { match: /^历史奖励补发 · (\d+) 小局$/, replace: n => 'Recompensas históricas pendientes · ' + q(n, 'mano', 'manos') },
    { match: /^已拥有 (\d+) 件$/, replace: n => 'Ya tienes ' + q(n, 'objeto', 'objetos') },
    // Reviewed dynamic templates are appended here.
    ...createSpanishDynamicPatterns(translate),
  ];
}

// BEGIN REVIEWED DYNAMIC TEMPLATES
const spanishMcrPatternNames = ["大四喜","大三元","绿一色","九莲宝灯","四杠","连七对","十三幺","清幺九","小四喜","小三元","字一色","四暗刻","一色双龙会","一色四同顺","一色四节高","一色四步高","三杠","混幺九","七对","七星不靠","全双刻","清一色","一色三同顺","一色三节高","全大","全中","全小","清龙","三色双龙会","一色三步高","全带五","三同刻","三暗刻","全不靠","组合龙","大于五","小于五","三风刻","花龙","推不倒","三色三同顺","三色三节高","无番和","妙手回春","海底捞月","杠上开花","抢杠和","双暗杠","碰碰和","混一色","三色三步高","五门齐","全求人","双箭刻","全带幺","不求人","双明杠","和绝张","箭刻","圈风刻","门风刻","门前清","平和","四归一","双同刻","双暗刻","暗杠","断幺","一般高","喜相逢","连六","老少副","幺九刻","明杠","缺一门","无字","边张","坎张","单钓将","自摸","花牌"];
const spanishDynamicDefinitions = [
  {
    "id": 142,
    "source": "删除「{0}」？删除后不能再用此密钥开房，不返还今日创建次数。历史信息和对局仍保留在数据站。",
    "target": "¿Eliminar «{0}»? Ya no se podrá crear salas con esta clave y no se recuperará el cupo diario consumido. El historial y las partidas seguirán en el portal de datos.",
    "re": "^删除「([^\\r\\n]+?)」？删除后不能再用此密钥开房，不返还今日创建次数。历史信息和对局仍保留在数据站。$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 90,
    "source": "已创建 {0} / {1} 个八人复式房间，后续创建已停止。{2}。请核对房间列表后再创建。",
    "target": "Salas de duplicado de ocho jugadores creadas: {0} / {1}; se ha detenido la creación. {2}. Comprueba la lista de salas antes de crear más.",
    "re": "^已创建 ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 个八人复式房间，后续创建已停止。([^\\r\\n]+?)。请核对房间列表后再创建。$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "n",
      "1": "n",
      "2": "ui"
    }
  },
  {
    "id": 8,
    "source": "赛事已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。",
    "target": "El torneo está cerrado y no permite crear salas. Para reabrirlo, envía una solicitud para revisión por un administrador de la plataforma.",
    "re": "^赛事已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 8,
    "source": "基地已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。",
    "target": "El club está cerrado y no permite crear salas. Para reabrirlo, envía una solicitud para revisión por un administrador de la plataforma.",
    "re": "^基地已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 91,
    "source": "已创建 {0} / {1} 个房间，后续创建已停止。{2}{3}。请核对房间列表后再创建。",
    "target": "Salas creadas: {0} / {1}; se ha detenido la creación. {2}{3}. Comprueba la lista de salas antes de crear más.",
    "re": "^已创建 ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 个房间，后续创建已停止。((?:已生成 [\\d]+ 个密钥，可在赛事与基地复式密钥中查看并复用。)?)([^\\r\\n]+?)。请核对房间列表后再创建。$",
    "captures": [
      0,
      1,
      2,
      3
    ],
    "types": {
      "0": "n",
      "1": "n",
      "2": "ui",
      "3": "ui"
    }
  },
  {
    "id": 131,
    "source": "当前限额无法完全下载所有牌谱数据（将{0} {1} / {2} 局）。是否确定？",
    "target": "El cupo actual no permite descargar todos los registros (se van a {0} {1} / {2} manos). ¿Continuar?",
    "re": "^当前限额无法完全下载所有牌谱数据（将((?:下载|下载到本机|下载到数据站缓存|下载ZIP|下载 ZIP|打包下载)) ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 局）。是否确定？$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 16,
    "source": "确认关闭赛事「{1}」？关闭后将被封存，再次开启需平台管理员审核",
    "target": "¿Cerrar el torneo «{1}»? Se archivará y su reapertura necesitará revisión de un administrador de la plataforma.",
    "re": "^确认关闭赛事「([^\\r\\n]+?)」？关闭后将被封存，再次开启需平台管理员审核$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 16,
    "source": "确认关闭基地「{1}」？关闭后将被封存，再次开启需平台管理员审核",
    "target": "¿Cerrar el club «{1}»? Se archivará y su reapertura necesitará revisión de un administrador de la plataforma.",
    "re": "^确认关闭基地「([^\\r\\n]+?)」？关闭后将被封存，再次开启需平台管理员审核$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 89,
    "source": "已生成 {0} 个密钥，可在赛事与基地复式密钥中查看并复用。",
    "target": "Se han generado {0} claves; puedes consultarlas y reutilizarlas en las claves de duplicado de torneos y clubes.",
    "re": "^已生成 ([+−\\-]?[\\d,.]+) 个密钥，可在赛事与基地复式密钥中查看并复用。$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 6,
    "source": "赛事已开启。赛事结束后可关闭赛事，赛事数据仍将保留。",
    "target": "El torneo está abierto. Puedes cerrar el torneo al finalizar el torneo; los datos del torneo se conservarán.",
    "re": "^赛事已开启。赛事结束后可关闭赛事，赛事数据仍将保留。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 6,
    "source": "基地已开启。基地结束后可关闭基地，基地数据仍将保留。",
    "target": "El club está abierto. Puedes cerrar el club al finalizar el club; los datos del club se conservarán.",
    "re": "^基地已开启。基地结束后可关闭基地，基地数据仍将保留。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 128,
    "source": "今日牌谱下载已达上限（{0}/{1} 局，凌晨 4 点刷新）",
    "target": "Se ha alcanzado el límite diario de descarga ({0}/{1} manos; se renueva a las 4 de la madrugada)",
    "re": "^今日牌谱下载已达上限（([+−\\-]?[\\d,.]+)/([+−\\-]?[\\d,.]+) 局，凌晨 4 点刷新）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 117,
    "source": "Botzone 互转仅支持国标，当前 rule={0}",
    "target": "La conversión de Botzone solo admite MCR; regla actual: rule={0}",
    "re": "^Botzone 互转仅支持国标，当前 rule=([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 5,
    "source": "赛事注册成功，开启后可创建房间、审核报名并组桌。",
    "target": "El torneo se ha registrado. Al abrirlo podrás crear salas, revisar inscripciones y formar mesas.",
    "re": "^赛事注册成功，开启后可创建房间、审核报名并组桌。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 5,
    "source": "基地注册成功，开启后可创建房间、审核报名并组桌。",
    "target": "El club se ha registrado. Al abrirlo podrás crear salas, revisar inscripciones y formar mesas.",
    "re": "^基地注册成功，开启后可创建房间、审核报名并组桌。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 125,
    "source": "雀渣小局 {0} 缺少 script / step",
    "target": "La mano {0} de Quezha no contiene script / step",
    "re": "^雀渣小局 ([^\\r\\n]*) 缺少 script / step$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 118,
    "source": "MJAI 互转仅支持日麻，当前 rule={0}",
    "target": "La conversión de MJAI solo admite Riichi; regla actual: rule={0}",
    "re": "^MJAI 互转仅支持日麻，当前 rule=([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 44,
    "source": "{0} 的 PT 须小于升段分 {1}（最高可设 {2}）",
    "target": "Los PT de {0} deben ser inferiores al umbral de ascenso {1} (máximo configurable: {2})",
    "re": "^((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段)) 的 PT 须小于升段分 ([+−\\-]?[\\d,.]+)（最高可设 ([+−\\-]?[\\d,.]+)）$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 150,
    "source": "{0} 次猜测机会已用尽，可以再来一局继续挑战",
    "target": "Has agotado {0} intentos; puedes jugar otra mano para seguir el desafío",
    "re": "^([+−\\-]?[\\d,.]+) 次猜测机会已用尽，可以再来一局继续挑战$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 36,
    "source": "今日剩余 {0} 个密钥额度，本次需要 {1} 个",
    "target": "Queda cupo para {0} claves hoy; esta operación necesita {1}",
    "re": "^今日剩余 ([+−\\-]?[\\d,.]+) 个密钥额度，本次需要 ([+−\\-]?[\\d,.]+) 个$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 37,
    "source": "剩余可保存 {0} 个密钥，本次需要 {1} 个",
    "target": "Se pueden guardar {0} claves más; esta operación necesita {1}",
    "re": "^剩余可保存 ([+−\\-]?[\\d,.]+) 个密钥，本次需要 ([+−\\-]?[\\d,.]+) 个$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 168,
    "source": "手牌须为 12～14 张，当前 {0} 张",
    "target": "La mano debe tener 12～14 fichas; actualmente {0}",
    "re": "^手牌须为 12～14 张，当前 ([+−\\-]?[\\d,.]+) 张$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 109,
    "source": "{0} · {1} · 局时 {2}s · 步时 {3}s{4}",
    "target": "{0} · {1} · Reserva por mano: {2}s · Tiempo por acción: {3}s{4}",
    "re": "^([^\\r\\n]+?) · ([^\\r\\n]+?) · 局时 ([+−\\-]?[\\d,.]+)s · 步时 ([+−\\-]?[\\d,.]+)s((?: · [^\\r\\n]+)?)$",
    "captures": [
      0,
      1,
      2,
      3,
      4
    ],
    "types": {
      "0": "ui",
      "1": "ui",
      "2": "n",
      "3": "n",
      "4": "ui"
    }
  },
  {
    "id": 108,
    "source": "{0} · {1} · {2} · 起始40000点",
    "target": "{0} · {1} · {2} · 40000 puntos iniciales",
    "re": "^([^\\r\\n]+?) · ([^\\r\\n]+?) · ([^\\r\\n]+?) · 起始40000点$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "ui",
      "2": "ui"
    }
  },
  {
    "id": 33,
    "source": "{0}局 · 已下载 {1}/{2}，点击补下缺失",
    "target": "{0} manos · Descargadas {1}/{2}; pulsa para descargar las que faltan",
    "re": "^([+−\\-]?[\\d,.]+)局 · 已下载 ([+−\\-]?[\\d,.]+)/([+−\\-]?[\\d,.]+)，点击补下缺失$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "n",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 42,
    "source": "{0} 的 PT 固定为 {1}，不再升降段",
    "target": "Los PT de {0} están fijados en {1}; ya no se sube ni baja de rango",
    "re": "^((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段)) 的 PT 固定为 ([+−\\-]?[\\d,.]+)，不再升降段$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 102,
    "source": "{0} · {1}圈 · SPGG 2024",
    "target": "{0} · {1} rondas · SPGG 2024",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)圈 · SPGG 2024$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 167,
    "source": "合计不能超过 {0} 张（当前 {1} 张）",
    "target": "El total no puede superar {0} fichas (actualmente {1})",
    "re": "^合计不能超过 ([+−\\-]?[\\d,.]+) 张（当前 ([+−\\-]?[\\d,.]+) 张）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 149,
    "source": "{0} 秒时间到，可以再来一局继续挑战",
    "target": "Tiempo agotado ({0} segundos); puedes jugar otra mano para seguir el desafío",
    "re": "^([+−\\-]?[\\d,.]+) 秒时间到，可以再来一局继续挑战$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 170,
    "source": "无法读取目录，请先启动 lab。{0}",
    "target": "No se pudo leer el catálogo. Inicia lab primero. {0}",
    "re": "^无法读取目录，请先启动 lab。([^\\r\\n]*)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 185,
    "source": "补花轮结束时平均向听数 · {0} 局",
    "target": "Shanten medio al terminar la reposición inicial de flores · {0} manos",
    "re": "^补花轮结束时平均向听数 · ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 7,
    "source": "赛事已关闭，重新开启申请审核中。",
    "target": "El torneo está cerrado; la solicitud de reapertura está en revisión.",
    "re": "^赛事已关闭，重新开启申请审核中。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 7,
    "source": "基地已关闭，重新开启申请审核中。",
    "target": "El club está cerrado; la solicitud de reapertura está en revisión.",
    "re": "^基地已关闭，重新开启申请审核中。$",
    "captures": [],
    "types": {}
  },
  {
    "id": 101,
    "source": "{0} · 十六张 · {1}局 · 底分{2}",
    "target": "{0} · Dieciséis fichas · {1} manos · Base {2}",
    "re": "^([^\\r\\n]+?) · 十六张 · ([+−\\-]?[\\d,.]+)局 · 底分([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 177,
    "source": "升段进度 {0} / {1}，还差 {2} PT",
    "target": "Progreso de ascenso: {0} / {1}; faltan {2} PT",
    "re": "^升段进度 ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+)，还差 ([+−\\-]?[\\d,.]+) PT$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "n",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 98,
    "source": "{0} · MIL 2023 · {1}局",
    "target": "{0} · MIL 2023 · {1} manos",
    "re": "^([^\\r\\n]+?) · MIL 2023 · ([+−\\-]?[\\d,.]+)局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 99,
    "source": "{0} · MIL 2025 · {1}局",
    "target": "{0} · MIL 2025 · {1} manos",
    "re": "^([^\\r\\n]+?) · MIL 2025 · ([+−\\-]?[\\d,.]+)局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 100,
    "source": "{0} · MIL 2024 · {1}局",
    "target": "{0} · MIL 2024 · {1} manos",
    "re": "^([^\\r\\n]+?) · MIL 2024 · ([+−\\-]?[\\d,.]+)局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 106,
    "source": "{0} · MIL 2024 · {1}圈",
    "target": "{0} · MIL 2024 · {1} rondas",
    "re": "^([^\\r\\n]+?) · MIL 2024 · ([+−\\-]?[\\d,.]+)圈$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 107,
    "source": "{0} · MIL 2026 · {1}副",
    "target": "{0} · MIL 2026 · {1} repartos",
    "re": "^([^\\r\\n]+?) · MIL 2026 · ([+−\\-]?[\\d,.]+)副$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 161,
    "source": "{0} · 你正在匹配（队列 {1} 人）",
    "target": "{0} · Buscando partida (cola: {1} jugadores)",
    "re": "^((?:国标\\+立直|立直)) · 你正在匹配（队列 ([+−\\-]?[\\d,.]+) 人）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 71,
    "source": "{0}{1} · {2} 人 · {3} · {4} · {5}",
    "target": "{0}{1} · {2} jugadores · {3} · {4} · {5}",
    "re": "^([^\\r\\n]+?)((?:·(?:双打|复式双打|三麻|鹫巢|鹫巢双打))?) · ([+−\\-]?[\\d,.]+) 人 · ([^\\r\\n]+?) · ([^\\r\\n]+?) · ([^\\r\\n]+?)$",
    "captures": [
      0,
      1,
      2,
      3,
      4,
      5
    ],
    "types": {
      "0": "ui",
      "1": "ui",
      "2": "n",
      "3": "ui",
      "4": "ui",
      "5": "ui"
    }
  },
  {
    "id": 83,
    "source": "请补齐完整牌墙（{0} / {1} 张）",
    "target": "Completa el muro entero ({0} / {1} fichas)",
    "re": "^请补齐完整牌墙（([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 张）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 130,
    "source": "本次需要 {0} 局，剩余 {1} 局。",
    "target": "Se necesitan {0} manos; quedan {1}.",
    "re": "^本次需要 ([+−\\-]?[\\d,.]+) 局，剩余 ([+−\\-]?[\\d,.]+) 局。$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 121,
    "source": "Botzone 无法映射: {0}",
    "target": "Botzone no puede convertir: {0}",
    "re": "^Botzone 无法映射: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 122,
    "source": "非法 Botzone 牌: {0}",
    "target": "Ficha Botzone no válida: {0}",
    "re": "^非法 Botzone 牌: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 127,
    "source": "当前为 {0}，雀渣互转仅支持国标",
    "target": "La regla actual es {0}; la conversión de Quezha solo admite MCR",
    "re": "^当前为 ([^\\r\\n]+?)，雀渣互转仅支持国标$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 179,
    "source": "请求过于频繁，请 {0} 秒后再试",
    "target": "Demasiadas solicitudes. Reintenta dentro de {0} segundos.",
    "re": "^请求过于频繁，请 ([+−\\-]?[\\d,.]+) 秒后再试$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 97,
    "source": "{0} · {1}局 · 开杠{2}张 · {3}",
    "target": "{0} · {1} manos · Reposición de kong: {2} fichas · {3}",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)局 · 开杠([+−\\-]?[\\d,.]+)张 · ((?:卡摸|小胡可点和))$",
    "captures": [
      0,
      1,
      2,
      3
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n",
      "3": "ui"
    }
  },
  {
    "id": 26,
    "source": "确认整组删除房间 {0} 和 {1}？",
    "target": "¿Eliminar juntas las salas {0} y {1}?",
    "re": "^确认整组删除房间 ([^\\r\\n]+?) 和 ([^\\r\\n]+?)？$",
    "captures": [
      0,
      1
    ],
    "types": {}
  },
  {
    "id": 192,
    "source": "已分析 {0} 局 · {1} 次和牌",
    "target": "{0} manos analizadas · {1} victorias",
    "re": "^已分析 ([+−\\-]?[\\d,.]+) 局 · ([+−\\-]?[\\d,.]+) 次和牌$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 19,
    "source": "确认申请重新开启赛事「{1}」？",
    "target": "¿Solicitar la reapertura del torneo «{1}»?",
    "re": "^确认申请重新开启赛事「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 19,
    "source": "确认申请重新开启基地「{1}」？",
    "target": "¿Solicitar la reapertura del club «{1}»?",
    "re": "^确认申请重新开启基地「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 21,
    "source": "确认移除赛事子管理员「{1}」？",
    "target": "¿Eliminar al administrador auxiliar del torneo «{1}»?",
    "re": "^确认移除赛事子管理员「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 21,
    "source": "确认移除基地子管理员「{1}」？",
    "target": "¿Eliminar al administrador auxiliar del club «{1}»?",
    "re": "^确认移除基地子管理员「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 4,
    "source": "赛事未开启，自动匹配已暂停",
    "target": "El torneo no está abierto; el emparejamiento automático está pausado",
    "re": "^赛事未开启，自动匹配已暂停$",
    "captures": [],
    "types": {}
  },
  {
    "id": 4,
    "source": "基地未开启，自动匹配已暂停",
    "target": "El club no está abierto; el emparejamiento automático está pausado",
    "re": "^基地未开启，自动匹配已暂停$",
    "captures": [],
    "types": {}
  },
  {
    "id": 9,
    "source": "未找到该玩家在本赛事的对局",
    "target": "No se han encontrado partidas de este jugador en este torneo",
    "re": "^未找到该玩家在本赛事的对局$",
    "captures": [],
    "types": {}
  },
  {
    "id": 9,
    "source": "未找到该玩家在本基地的对局",
    "target": "No se han encontrado partidas de este jugador en este club",
    "re": "^未找到该玩家在本基地的对局$",
    "captures": [],
    "types": {}
  },
  {
    "id": 115,
    "source": "{0}的数量须为 1–{1} 的整数",
    "target": "La cantidad de {0} debe ser un entero entre 1 y {1}",
    "re": "^([^\\r\\n]+?)的数量须为 1–([+−\\-]?[\\d,.]+) 的整数$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "n"
    }
  },
  {
    "id": 148,
    "source": "剩余 {0} 次 · 第 {1} 猜",
    "target": "Quedan {0} intentos · Intento {1}",
    "re": "^剩余 ([+−\\-]?[\\d,.]+) 次 · 第 ([+−\\-]?[\\d,.]+) 猜$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 157,
    "source": "第 {0} 局 · 剩余 {1} 次",
    "target": "Mano {0} · Quedan {1} intentos",
    "re": "^第 ([+−\\-]?[\\d,.]+) 局 · 剩余 ([+−\\-]?[\\d,.]+) 次$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 159,
    "source": "进入匹配（{0} · 队列 {1}）",
    "target": "Buscar partida ({0} · Cola: {1})",
    "re": "^进入匹配（((?:国标\\+立直|立直)) · 队列 ([+−\\-]?[\\d,.]+)）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 23,
    "source": "已创建 {0} 个八人复式房间",
    "target": "Se han creado {0} salas de duplicado de ocho jugadores",
    "re": "^已创建 ([+−\\-]?[\\d,.]+) 个八人复式房间$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 152,
    "source": "{0}退出了对局，本场直接结束",
    "target": "{0} ha abandonado; la partida termina inmediatamente",
    "re": "^([^\\r\\n]+?)退出了对局，本场直接结束$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 163,
    "source": "{0} · 匹配队列（0 人）",
    "target": "{0} · Cola de emparejamiento (0 jugadores)",
    "re": "^((?:国标\\+立直|立直)) · 匹配队列（0 人）$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 181,
    "source": "已下载 {0} 局到数据站缓存",
    "target": "{0} manos descargadas a la caché del portal de datos",
    "re": "^已下载 ([+−\\-]?[\\d,.]+) 局到数据站缓存$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 68,
    "source": "吃牌成功，请打出一张手牌",
    "target": "Chow completado; descarta una ficha de la mano",
    "re": "^吃牌成功，请打出一张手牌$",
    "captures": [],
    "types": {}
  },
  {
    "id": 68,
    "source": "碰牌成功，请打出一张手牌",
    "target": "Pung completado; descarta una ficha de la mano",
    "re": "^碰牌成功，请打出一张手牌$",
    "captures": [],
    "types": {}
  },
  {
    "id": 84,
    "source": "请补齐 {0}（{1} / {2} 张）",
    "target": "Completa {0} ({1} / {2} fichas)",
    "re": "^请补齐 ((?:公共牌墙|[1-4] 号玩家(?:手牌|牌山)))（([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 张）$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 105,
    "source": "{0} · {1}局 · 满番叠加{2}",
    "target": "{0} · {1} manos · Límites acumulables: {2}",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)局 · 满番叠加((?:开启|关闭))$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "ui"
    }
  },
  {
    "id": 43,
    "source": "{0} 的 PT 不能低于 {1}",
    "target": "Los PT de {0} no pueden ser inferiores a {1}",
    "re": "^((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段)) 的 PT 不能低于 ([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 153,
    "source": "{0} 赢得本场 BO{1} 对战",
    "target": "{0} gana este duelo BO{1}",
    "re": "^([^\\r\\n]+?) 赢得本场 BO([+−\\-]?[\\d,.]+) 对战$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "n"
    }
  },
  {
    "id": 162,
    "source": "{0} · 匹配队列（{1} 人）",
    "target": "{0} · Cola de emparejamiento ({1} jugadores)",
    "re": "^((?:国标\\+立直|立直)) · 匹配队列（([+−\\-]?[\\d,.]+) 人）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 184,
    "source": "配牌+摸牌含花概率 {0}/{1}",
    "target": "Probabilidad de flores en reparto + robos: {0}/{1}",
    "re": "^配牌\\+摸牌含花概率 ([+−\\-]?[\\d,.]+)/([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 114,
    "source": "{0}不能包含标签或控制字符",
    "target": "{0} no puede contener etiquetas ni caracteres de control",
    "re": "^((?:发件人|标题|正文|操作备注))不能包含标签或控制字符$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 123,
    "source": "MJAI 无法映射: {0}",
    "target": "MJAI no puede convertir: {0}",
    "re": "^MJAI 无法映射: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 124,
    "source": "非法 MJAI 牌: {0}",
    "target": "Ficha MJAI no válida: {0}",
    "re": "^非法 MJAI 牌: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 166,
    "source": "副露 #{0} 不是合法牌组",
    "target": "El grupo expuesto #{0} no es válido",
    "re": "^副露 #([+−\\-]?[\\d,.]+) 不是合法牌组$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 72,
    "source": "{0}须为 {1}–{2} 之间的整数",
    "target": "{0} debe ser un entero entre {1} y {2}",
    "re": "^([^\\r\\n]+?)须为 ([+−\\-]?[\\d,.]+)–([+−\\-]?[\\d,.]+) 之间的整数$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 104,
    "source": "{0} · {1}圈 · 七小对{2}",
    "target": "{0} · {1} rondas · Siete parejas: {2}",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)圈 · 七小对((?:开启|关闭))$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "ui"
    }
  },
  {
    "id": 95,
    "source": "{0}须为 0–{1} 秒的整数",
    "target": "{0} debe ser un entero de 0 a {1} segundos",
    "re": "^([^\\r\\n]+?)须为 0–([+−\\-]?[\\d,.]+) 秒的整数$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 134,
    "source": "剩余 {0} 小时 {1} 分钟",
    "target": "Restante: {0} horas y {1} minutos",
    "re": "^剩余 ([+−\\-]?[\\d,.]+) 小时 ([+−\\-]?[\\d,.]+) 分钟$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 176,
    "source": "今日已用 {0} / {1} 局",
    "target": "Manos usadas hoy: {0} / {1}",
    "re": "^今日已用 ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 178,
    "source": "{0} 枚 · {1} 枚/和牌",
    "target": "{0} fichas · {1} fichas/victoria",
    "re": "^([+−\\-]?[\\d,.]+) 枚 · ([+−\\-]?[\\d,.]+) 枚/和牌$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 10,
    "source": "确认通过「{1}」的报名？",
    "target": "¿Aprobar la inscripción de «{1}»?",
    "re": "^确认通过「([^\\r\\n]+?)」的报名？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 10,
    "source": "确认拒绝「{1}」的报名？",
    "target": "¿Rechazar la inscripción de «{1}»?",
    "re": "^确认拒绝「([^\\r\\n]+?)」的报名？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 88,
    "source": "{0}不能使用普通复式牌墙",
    "target": "{0} no puede usar un muro de duplicado ordinario",
    "re": "^((?:透明国标|国标十六张|四癞子))不能使用普通复式牌墙$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 119,
    "source": "非法雀渣牌 id: {0}",
    "target": "ID de ficha Quezha no válido: {0}",
    "re": "^非法雀渣牌 id: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 171,
    "source": "将运行 {0} 条，确认？",
    "target": "Se ejecutarán {0} casos. ¿Continuar?",
    "re": "^将运行 ([+−\\-]?[\\d,.]+) 条，确认？$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 180,
    "source": "已下载 {0} 局 ZIP",
    "target": "ZIP de {0} manos descargado",
    "re": "^已下载 ([+−\\-]?[\\d,.]+) 局 ZIP$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 47,
    "source": "纯赖子明杠×{0}（{1}番）",
    "target": "Kong expuesto solo de comodines ×{0} ({1} fan)",
    "re": "^纯赖子明杠×([+−\\-]?[\\d,.]+)（([+−\\-]?[\\d,.]+)番）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 48,
    "source": "纯赖子暗杠×{0}（{1}番）",
    "target": "Kong oculto solo de comodines ×{0} ({1} fan)",
    "re": "^纯赖子暗杠×([+−\\-]?[\\d,.]+)（([+−\\-]?[\\d,.]+)番）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 132,
    "source": "是否确定{0}这 {1} 局？",
    "target": "¿{0} {1} manos?",
    "re": "^是否确定((?:下载|下载到本机|下载到数据站缓存|下载ZIP|下载 ZIP|打包下载))这 ([+−\\-]?[\\d,.]+) 局？$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 133,
    "source": "剩余 {0} 天 {1} 小时",
    "target": "Restante: {0} días y {1} horas",
    "re": "^剩余 ([+−\\-]?[\\d,.]+) 天 ([+−\\-]?[\\d,.]+) 小时$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 145,
    "source": "A队 {0} / B队 {1}",
    "target": "Equipo A {0} / Equipo B {1}",
    "re": "^A队 ([+−\\-]?[\\d,.]+) / B队 ([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 155,
    "source": "{0} 猜中 · 答案 {1}",
    "target": "{0} ha acertado · Respuesta: {1}",
    "re": "^([^\\r\\n]+?) 猜中 · 答案 ([^\\r\\n]+?)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "ui"
    }
  },
  {
    "id": 158,
    "source": "匹配中（{0} · {1}）…",
    "target": "Buscando partida ({0} · {1})…",
    "re": "^匹配中（((?:国标\\+立直|立直)) · ([+−\\-]?[\\d,.]+)）…$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 183,
    "source": "将分析 {0} / {1} 局",
    "target": "Manos que se analizarán: {0} / {1}",
    "re": "^将分析 ([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 13,
    "source": "确认开启赛事「{1}」？",
    "target": "¿Abrir el torneo «{1}»?",
    "re": "^确认开启赛事「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 13,
    "source": "确认开启基地「{1}」？",
    "target": "¿Abrir el club «{1}»?",
    "re": "^确认开启基地「([^\\r\\n]+?)」？$",
    "captures": [
      1
    ],
    "types": {}
  },
  {
    "id": 120,
    "source": "无法映射到雀渣: {0}",
    "target": "No se puede convertir a Quezha: {0}",
    "re": "^无法映射到雀渣: ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 143,
    "source": "使用 1 个「{0}」？",
    "target": "¿Usar 1 unidad de «{0}»?",
    "re": "^使用 1 个「([^\\r\\n]+?)」？$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 187,
    "source": "已下载 {0} 局到本机",
    "target": "{0} manos descargadas a este dispositivo",
    "re": "^已下载 ([+−\\-]?[\\d,.]+) 局到本机$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 20,
    "source": "赛事子管理员已添加",
    "target": "Administrador auxiliar del torneo añadido",
    "re": "^赛事子管理员已添加$",
    "captures": [],
    "types": {}
  },
  {
    "id": 20,
    "source": "基地子管理员已添加",
    "target": "Administrador auxiliar del club añadido",
    "re": "^基地子管理员已添加$",
    "captures": [],
    "types": {}
  },
  {
    "id": 92,
    "source": "{0}须为 {1}–{2} 的整数",
    "target": "{0} debe ser un entero entre {1} y {2}",
    "re": "^([^\\r\\n]+?)须为 ([+−\\-]?[\\d,.]+)–([+−\\-]?[\\d,.]+) 的整数$",
    "captures": [
      0,
      1,
      2
    ],
    "types": {
      "0": "ui",
      "1": "n",
      "2": "n"
    }
  },
  {
    "id": 24,
    "source": "已创建 {0} 个房间{1}",
    "target": "Se han creado {0} salas{1}",
    "re": "^已创建 ([+−\\-]?[\\d,.]+) 个房间((?:及 \\d+ 个复式密钥)?)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "ui"
    }
  },
  {
    "id": 49,
    "source": "金银赤宝牌（{0}{1}番）",
    "target": "Dora dorados, plateados y rojos ({0}{1} fan)",
    "re": "^金银赤宝牌（([+−\\-]?)([+−\\-]?[\\d,.]+)番）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "n"
    }
  },
  {
    "id": 78,
    "source": "{0} 番 · 底分 {1}",
    "target": "{0} puntos · Base {1}",
    "re": "^([+−\\-]?[\\d,.]+) 番 · 底分 ([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 25,
    "source": "及 {0} 个复式密钥",
    "target": "y {0} claves de duplicado",
    "re": "^及 ([+−\\-]?[\\d,.]+) 个复式密钥$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 94,
    "source": "自定义台值 · {0}",
    "target": "Valor tai personalizado · {0}",
    "re": "^自定义台值 · ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 156,
    "source": "平局 · 答案 {0}",
    "target": "Empate · Respuesta: {0}",
    "re": "^平局 · 答案 ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 22,
    "source": "移除赛事子管理员",
    "target": "Eliminar administrador auxiliar del torneo",
    "re": "^移除赛事子管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 22,
    "source": "移除基地子管理员",
    "target": "Eliminar administrador auxiliar del club",
    "re": "^移除基地子管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 59,
    "source": "点和{0}倍\n自摸{1}倍",
    "target": "Por descarte ×{0}\nDel muro ×{1}",
    "re": "^点和([+−\\-]?[\\d,.]+)倍\n自摸([+−\\-]?[\\d,.]+)倍$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 65,
    "source": "点和{0}分\n自摸{1}分",
    "target": "Por descarte: {0} puntos\nDel muro: {1} puntos",
    "re": "^点和([\\d.]+(?:[–～\\-][\\d.]+)?)分\n自摸([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "n"
    }
  },
  {
    "id": 110,
    "source": "{0}须为0–{1}的整数",
    "target": "{0} debe ser un entero de 0 a {1}",
    "re": "^([^\\r\\n]+?)须为0–([+−\\-]?[\\d,.]+)的整数$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 113,
    "source": "{0}须为 1–{1} 字",
    "target": "{0} debe tener entre 1 y {1} caracteres",
    "re": "^((?:发件人|标题|正文|操作备注))须为 1–([+−\\-]?[\\d,.]+) 字$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 129,
    "source": "本次将{0} {1} 局。",
    "target": "Se van a {0} {1} manos.",
    "re": "^本次将((?:下载|下载到本机|下载到数据站缓存|下载ZIP|下载 ZIP|打包下载)) ([+−\\-]?[\\d,.]+) 局。$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 165,
    "source": "{0} 加入副露 #{1}",
    "target": "Añadir {0} al grupo expuesto #{1}",
    "re": "^([A-G][XY][1-9]) 加入副露 #([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 31,
    "source": "确认删除「{0}」？",
    "target": "¿Eliminar «{0}»?",
    "re": "^确认删除「([^\\r\\n]+?)」？$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 32,
    "source": "{0}局 · 已下载",
    "target": "{0} manos · Descargadas",
    "re": "^([+−\\-]?[\\d,.]+)局 · 已下载$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 146,
    "source": "{0}大胡（鸟另计）",
    "target": "{0} jugadas grandes (pájaros aparte)",
    "re": "^([+−\\-]?[\\d,.]+)大胡（鸟另计）$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 193,
    "source": "找到 {0} 次和牌",
    "target": "Se han encontrado {0} victorias",
    "re": "^找到 ([+−\\-]?[\\d,.]+) 次和牌$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 35,
    "source": "请填写赛事名称",
    "target": "Introduce el nombre del torneo",
    "re": "^请填写赛事名称$",
    "captures": [],
    "types": {}
  },
  {
    "id": 35,
    "source": "请填写基地名称",
    "target": "Introduce el nombre del club",
    "re": "^请填写基地名称$",
    "captures": [],
    "types": {}
  },
  {
    "id": 2,
    "source": "已选{0}第 {1} 张",
    "target": "Seleccionada la ficha {1} de {0}",
    "re": "^已选([东南西北]位(?:手牌|牌山|个人牌墙|固定牌墙|公共牌墙))第 ([+−\\-]?[\\d,.]+) 张$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 50,
    "source": "纯赖子明杠×{0}",
    "target": "Kong expuesto solo de comodines ×{0}",
    "re": "^纯赖子明杠×([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 51,
    "source": "纯赖子暗杠×{0}",
    "target": "Kong oculto solo de comodines ×{0}",
    "re": "^纯赖子暗杠×([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 56,
    "source": "无癞子翻倍*{0}",
    "target": "Duplicación sin comodines*{0}",
    "re": "^无癞子翻倍\\*([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 93,
    "source": "请选择有效的{0}",
    "target": "Selecciona una opción válida de {0}",
    "re": "^请选择有效的([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 135,
    "source": "剩余 {0} 分钟",
    "target": "Quedan {0} minutos",
    "re": "^剩余 ([+−\\-]?[\\d,.]+) 分钟$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 154,
    "source": "第 {0} 局结束",
    "target": "Mano {0} terminada",
    "re": "^第 ([+−\\-]?[\\d,.]+) 局结束$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 160,
    "source": "进入匹配（{0}）",
    "target": "Buscar partida ({0})",
    "re": "^进入匹配（((?:国标\\+立直|立直))）$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 175,
    "source": "{0} 每日对局数",
    "target": "{0}: partidas diarias",
    "re": "^([^\\r\\n]+?) 每日对局数$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 188,
    "source": "已打包 {0} 局",
    "target": "{0} manos empaquetadas",
    "re": "^已打包 ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 189,
    "source": "已分析 {0} 局",
    "target": "{0} manos analizadas",
    "re": "^已分析 ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 190,
    "source": "0 / {0} 局",
    "target": "0 / {0} manos",
    "re": "^0 / ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 86,
    "source": "赛事主管理员",
    "target": "Administrador principal del torneo",
    "re": "^赛事主管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 86,
    "source": "基地主管理员",
    "target": "Administrador principal del club",
    "re": "^基地主管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 87,
    "source": "赛事子管理员",
    "target": "Administrador auxiliar del torneo",
    "re": "^赛事子管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 87,
    "source": "基地子管理员",
    "target": "Administrador auxiliar del club",
    "re": "^基地子管理员$",
    "captures": [],
    "types": {}
  },
  {
    "id": 30,
    "source": "{0} / {1} 人",
    "target": "{0} / {1} jugadores",
    "re": "^([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 人$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 62,
    "source": "\n自摸{0}番{1}分",
    "target": "\nDel muro: {0} fan, {1} puntos",
    "re": "^\n自摸([+−\\-]?[\\d,.]+)番([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 75,
    "source": "{0} 番 {1} 符",
    "target": "{0} han {1} fu",
    "re": "^([+−\\-]?[\\d,.]+) 番 ([+−\\-]?[\\d,.]+) 符$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 76,
    "source": "{0} 点 · {1}",
    "target": "{0} puntos · {1}",
    "re": "^([+−\\-]?[\\d,.]+) 点 · ([^\\r\\n]+?)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "ui"
    }
  },
  {
    "id": 111,
    "source": "{0}番\n自摸{1}分",
    "target": "{0} fan\nDel muro: {1} puntos",
    "re": "^([+−\\-]?[\\d,.]+)番\n自摸([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 191,
    "source": "{0} / {1} 局",
    "target": "{0} / {1} manos",
    "re": "^([+−\\-]?[\\d,.]+) / ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 45,
    "source": "仅点和\n{0}点",
    "target": "Solo ganar por descarte\n{0} puntos",
    "re": "^仅点和\n([+−\\-]?[\\d,.]+)点$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 46,
    "source": "仅自摸\n{0}点",
    "target": "Solo cerrar del muro\n{0} puntos",
    "re": "^仅自摸\n([+−\\-]?[\\d,.]+)点$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 64,
    "source": "仅自摸\n{0}分",
    "target": "Solo cerrar del muro\n{0} puntos",
    "re": "^仅自摸\n([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 73,
    "source": "{0}须为开关值",
    "target": "{0} debe ser un valor de activación",
    "re": "^([^\\r\\n]+?)须为开关值$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 140,
    "source": "安定 {0} 段",
    "target": "Rango estable: {0} dan",
    "re": "^安定 ([+−\\-]?[\\d,.]+) 段$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 147,
    "source": "{0}分（含鸟）",
    "target": "{0} puntos (con pájaros)",
    "re": "^([+−\\-]?[\\d,.]+)分（含鸟）$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 174,
    "source": "{0} 历史总计",
    "target": "{0}: total histórico",
    "re": "^([^\\r\\n]+?) 历史总计$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 182,
    "source": "剩余 {0} 局",
    "target": "Quedan {0} manos",
    "re": "^剩余 ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 15,
    "source": "赛事已开启",
    "target": "torneo abierto",
    "re": "^赛事已开启$",
    "captures": [],
    "types": {}
  },
  {
    "id": 15,
    "source": "基地已开启",
    "target": "club abierto",
    "re": "^基地已开启$",
    "captures": [],
    "types": {}
  },
  {
    "id": 18,
    "source": "赛事已关闭",
    "target": "torneo cerrado",
    "re": "^赛事已关闭$",
    "captures": [],
    "types": {}
  },
  {
    "id": 18,
    "source": "基地已关闭",
    "target": "club cerrado",
    "re": "^基地已关闭$",
    "captures": [],
    "types": {}
  },
  {
    "id": 3,
    "source": "对局 {0}\n{1}",
    "target": "Partida {0}\n{1}",
    "re": "^对局 ([^\\r\\n]+?)\n((?:确认强制暂停该对局？将在下一安全节点暂停。|确认恢复该对局？若尚在等待暂停，将取消暂停请求。|确认强制结束该对局？玩家将返回大厅，结束后无法继续，也不会保存为完整牌谱记录。))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "ui"
    }
  },
  {
    "id": 38,
    "source": "{0} 局（{1}）",
    "target": "{0} manos ({1})",
    "re": "^([+−\\-]?[\\d,.]+) 局（([^\\r\\n]+?)）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "ui"
    }
  },
  {
    "id": 58,
    "source": "自摸{0}番{1}分",
    "target": "Cerrar del muro: {0} fan, {1} puntos",
    "re": "^自摸([+−\\-]?[\\d,.]+)番([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 61,
    "source": "点和{0}番{1}分",
    "target": "Por descarte: {0} fan, {1} puntos",
    "re": "^点和([+−\\-]?[\\d,.]+)番([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 81,
    "source": "夺彩{0}采用{1}",
    "target": "Duocai {0} usa {1}",
    "re": "^夺彩((?:国标|立直))采用((?:全庄战（轮庄制）|东南战))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "ui"
    }
  },
  {
    "id": 85,
    "source": "{0} 号玩家{1}",
    "target": "{1} del jugador {0}",
    "re": "^([+−\\-]?[\\d,.]+) 号玩家((?:手牌|牌山))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "ui"
    }
  },
  {
    "id": 96,
    "source": "{0} · {1}局",
    "target": "{0} · {1} manos",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 103,
    "source": "{0} · {1}副",
    "target": "{0} · {1} repartos",
    "re": "^([^\\r\\n]+?) · ([+−\\-]?[\\d,.]+)副$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 186,
    "source": "{0}（{1} 局）",
    "target": "{0} ({1} manos)",
    "re": "^([+−\\-]?[\\d,.]+)（([+−\\-]?[\\d,.]+) 局）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 74,
    "source": "{0} 倍役满",
    "target": "Yakuman ×{0}",
    "re": "^([+−\\-]?[\\d,.]+) 倍役满$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 136,
    "source": "{0}不可进入",
    "target": "No puedes acceder a {0}",
    "re": "^((?:初级场|中级场|高级场|MCRPL))不可进入$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 151,
    "source": "{0} 猜中了",
    "target": "{0} ha acertado",
    "re": "^([^\\r\\n]+?) 猜中了$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 164,
    "source": "[{0}] 组",
    "target": "[{0}] grupos",
    "re": "^\\[((?:[\\d, ]+|全体))\\] 组$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 173,
    "source": " · {0}局",
    "target": " · {0} manos",
    "re": "^ · ([+−\\-]?[\\d,.]+)局$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 11,
    "source": "通过报名",
    "target": "Aprobar inscripción",
    "re": "^通过报名$",
    "captures": [],
    "types": {}
  },
  {
    "id": 11,
    "source": "拒绝报名",
    "target": "Rechazar inscripción",
    "re": "^拒绝报名$",
    "captures": [],
    "types": {}
  },
  {
    "id": 14,
    "source": "开启赛事",
    "target": "Abrir torneo",
    "re": "^开启赛事$",
    "captures": [],
    "types": {}
  },
  {
    "id": 14,
    "source": "开启基地",
    "target": "Abrir club",
    "re": "^开启基地$",
    "captures": [],
    "types": {}
  },
  {
    "id": 17,
    "source": "关闭赛事",
    "target": "Cerrar torneo",
    "re": "^关闭赛事$",
    "captures": [],
    "types": {}
  },
  {
    "id": 17,
    "source": "关闭基地",
    "target": "Cerrar club",
    "re": "^关闭基地$",
    "captures": [],
    "types": {}
  },
  {
    "id": 27,
    "source": "{0} 至 {1}",
    "target": "De {0} a {1}",
    "re": "^([0-9][0-9/.:\\-\\s年月日]*) 至 ([0-9][0-9/.:\\-\\s年月日]*)$",
    "captures": [
      0,
      1
    ],
    "types": {}
  },
  {
    "id": 34,
    "source": "{0} {1} 局",
    "target": "{0}: {1} manos",
    "re": "^((?:牌谱格式无效|重复牌谱|牌谱中没有目标玩家|非国标或规则不明|自定义、赛事或非天梯对局|无法确认实际天梯场次|天梯场次级别不明|局制不明或不支持|牌谱场次或局制信息冲突|缺少目标玩家的有效结算名次|缺少完整并列结算信息)) ([+−\\-]?[\\d,.]+) 局$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 52,
    "source": "{0}（{1}番）",
    "target": "{0} ({1} puntos)",
    "re": "^([^\\r\\n]+?)（([+−\\-]?[\\d,.]+)番）$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "n"
    }
  },
  {
    "id": 172,
    "source": "{0} 或 {1}",
    "target": "{0} o {1}",
    "re": "^([+−\\-]?[\\d,.]+) 或 ([+−\\-]?[\\d,.]+)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 41,
    "source": "{0}规则书",
    "target": "Reglamento de {0}",
    "re": "^((?:清章十三|新章十三（Wiki）|新章十三（恋绘色）|新章十三（恋绘色魔改）|新章十六))规则书$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 54,
    "source": "黑花{0}朵",
    "target": "{0} flores negras",
    "re": "^黑花([+−\\-]?[\\d,.]+)朵$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 55,
    "source": "花牌*{0}",
    "target": "Flores*{0}",
    "re": "^花牌\\*([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 57,
    "source": "自摸{0}番",
    "target": "Cerrar del muro: {0} puntos",
    "re": "^自摸([+−\\-]?[\\d,.]+)番$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 60,
    "source": "自摸{0}倍",
    "target": "Cerrar del muro ×{0}",
    "re": "^自摸([+−\\-]?[\\d,.]+)倍$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 66,
    "source": "你打出{0}",
    "target": "Has descartado {0}",
    "re": "^你打出((?:[1-9][万饼筒条]|[东南西北中发白]|红中|白板|[A-G][XY][1-9]))$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 112,
    "source": "红中→{0}",
    "target": "Dragón rojo → {0}",
    "re": "^红中→((?:[1-9][万饼筒条]|[东南西北中发白]|红中|白板|[A-G][XY][1-9]))$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 169,
    "source": "等待 {0}",
    "target": "Esperando a {0}",
    "re": "^等待 ([^\\r\\n]+?)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 12,
    "source": "已通过",
    "target": "Aprobado",
    "re": "^已通过$",
    "captures": [],
    "types": {}
  },
  {
    "id": 12,
    "source": "已拒绝",
    "target": "Rechazado",
    "re": "^已拒绝$",
    "captures": [],
    "types": {}
  },
  {
    "id": 63,
    "source": "{0}～{1}点",
    "target": "{0}～{1} puntos",
    "re": "^([+−\\-]?[\\d,.]+)～([+−\\-]?[\\d,.]+)点$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 67,
    "source": "{0}打出{1}",
    "target": "{0} descarta {1}",
    "re": "^([^\\r\\n]+?)打出((?:[1-9][万饼筒条]|[东南西北中发白]|红中|白板|[A-G][XY][1-9]))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "1": "ui"
    }
  },
  {
    "id": 116,
    "source": "{0}～{1}分",
    "target": "{0}～{1} puntos",
    "re": "^([+−\\-]?[\\d,.]+)～([+−\\-]?[\\d,.]+)分$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "n"
    }
  },
  {
    "id": 28,
    "source": "{0} 起",
    "target": "Desde {0}",
    "re": "^([0-9][0-9/.:\\-\\s年月日]*) 起$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 29,
    "source": "至 {0}",
    "target": "Hasta {0}",
    "re": "^至 ([0-9][0-9/.:\\-\\s年月日]*)$",
    "captures": [
      0
    ],
    "types": {}
  },
  {
    "id": 39,
    "source": "场风{0}",
    "target": "Viento prevalente: {0}",
    "re": "^场风([东南西北])$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 40,
    "source": "自风{0}",
    "target": "Viento de asiento: {0}",
    "re": "^自风([东南西北])$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 77,
    "source": "{0}役满",
    "target": "Yakuman {0}",
    "re": "^((?:\\d+ 倍)?)役满$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 79,
    "source": "{0}选择",
    "target": "{0} elige",
    "re": "^([东南西北]家)选择$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 138,
    "source": "安定{0}",
    "target": "Rango estable: {0}",
    "re": "^安定((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段))$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 139,
    "source": "低于{0}",
    "target": "Por debajo de {0}",
    "re": "^低于((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段))$",
    "captures": [
      0
    ],
    "types": {
      "0": "ui"
    }
  },
  {
    "id": 194,
    "source": "玩家{0}",
    "target": "Jugador {0}",
    "re": "^玩家([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 1,
    "source": "{0}位{1}",
    "target": "{1} de {0}",
    "re": "^([东南西北])位((?:手牌|牌山|个人牌墙|固定牌墙|公共牌墙))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "ui"
    }
  },
  {
    "id": 80,
    "source": "{0}分{1}",
    "target": "{0} puntos{1}",
    "re": "^([+−\\-]?[\\d,.]+)分((?:\\n禁小胡，须升级)?)$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "n",
      "1": "ui"
    }
  },
  {
    "id": 82,
    "source": "{0}风{1}",
    "target": "Ronda {0}, asiento {1}",
    "re": "^([东南西北])风([东南西北])$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "ui"
    }
  },
  {
    "id": 137,
    "source": "{0}需{1}",
    "target": "{0} requiere {1}",
    "re": "^((?:初级场|中级场|高级场|MCRPL))需((?:[1-9]\\d*级|[1-9]\\d*段|[初一二三四五六七八九十]段))$",
    "captures": [
      0,
      1
    ],
    "types": {
      "0": "ui",
      "1": "ui"
    }
  },
  {
    "id": 126,
    "source": "番{0}",
    "target": "Jugada {0}",
    "re": "^番([+−\\-]?[\\d,.]+)$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 141,
    "source": "{0}饼",
    "target": "{0} de círculos",
    "re": "^([+−\\-]?[\\d,.]+)饼$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  },
  {
    "id": 144,
    "source": "{0}月",
    "target": "Mes {0}",
    "re": "^((?:[1-9]|1[0-2]))月$",
    "captures": [
      0
    ],
    "types": {
      "0": "n"
    }
  }
];
function createSpanishDynamicPatterns(translate) {
  const number = value => Math.abs(Number(String(value).replace('−','-').replaceAll(',','')));
  const singularPairs = [
    ['manos','mano'],['fichas','ficha'],['puntos','punto'],['salas','sala'],['claves','clave'],
    ['jugadores','jugador'],['flores negras','flor negra'],['rondas','ronda'],['repartos','reparto'],
    ['días','día'],['horas','hora'],['minutos','minuto'],['segundos','segundo'],['intentos','intento'],
    ['casos','caso'],['jugadas grandes','jugada grande'],['victorias','victoria'],
  ];
  const wind = {东:'Este',南:'Sur',西:'Oeste',北:'Norte',东家:'Este',南家:'Sur',西家:'Oeste',北家:'Norte'};
  const zone = {手牌:'Mano',牌山:'Muro',个人牌墙:'Muro personal',固定牌墙:'Muro fijo',公共牌墙:'Muro común'};
  const action = {下载:'descargar',下载到本机:'descargar al dispositivo',下载到数据站缓存:'descargar a la caché del portal de datos',下载ZIP:'descargar en ZIP','下载 ZIP':'descargar en ZIP',打包下载:'empaquetar y descargar'};
  function ui(value) {
    if (Object.hasOwn(wind,value)) return wind[value];
    if (Object.hasOwn(zone,value)) return zone[value];
    if (Object.hasOwn(action,value)) return action[value];
    if (value === '\n禁小胡，须升级') return '\nMano pequeña no permitida; hay que mejorarla';
    if (value === '') return '';
    if (/^\d+ 倍$/.test(value)) return '×' + value.split(' ')[0];
    if (/^及 \d+ 个复式密钥$/.test(value)) return ' ' + translate(value);
    if (/^ · (?:复式|训练牌山|手动牌山|复现牌山|密钥牌山)$/.test(value)) return ' · ' + translate(value.slice(3));
    const variant = value.match(/^(国标|立直|青雀|四川|古典|长沙|红雀|红中|武汉|山西|福州|南昌|长春|广东)·(双打|复式双打|三麻|鹫巢|鹫巢双打)$/);
    if (variant) return translate(variant[1]) + '·' + translate(variant[2]);
    if (value.startsWith('·')) return '·' + translate(value.slice(1));
    return translate(value);
  }
  return spanishDynamicDefinitions.map(definition => ({
    match: new RegExp(definition.re, 's'),
    replace: (...captures) => {
      const raw = [];
      definition.captures.forEach((index, i) => { raw[index] = captures[i]; });
      let target = definition.target;
      for (const [index, kind] of Object.entries(definition.types)) {
        if (kind !== 'n' || number(raw[index]) !== 1) continue;
        const token = '{' + index + '}';
        for (const [many, one] of singularPairs) target = target.replaceAll(token + ' ' + many, token + ' ' + one);
        target = target.replaceAll(token + ' mano descargadas', token + ' mano descargada')
          .replaceAll(token + ' mano analizadas', token + ' mano analizada')
          .replaceAll(token + ' mano empaquetadas', token + ' mano empaquetada')
          .replaceAll(token + ' mano · Descargadas', token + ' mano · Descargada')
          .replaceAll('Quedan ' + token + ' ', 'Queda ' + token + ' ')
          .replaceAll('quedan ' + token + '.', 'queda ' + token + '.')
          .replaceAll('Se han creado ' + token + ' ', 'Se ha creado ' + token + ' ')
          .replaceAll('Se han generado ' + token + ' ', 'Se ha generado ' + token + ' ')
          .replaceAll('Se han encontrado ' + token + ' ', 'Se ha encontrado ' + token + ' ')
          .replaceAll('Se ejecutarán ' + token + ' ', 'Se ejecutará ' + token + ' ')
          .replaceAll('Se necesitan ' + token + ' ', 'Se necesita ' + token + ' ')
          .replaceAll('se van a {0} ' + token + ' ', 'se va a {0} ' + token + ' ')
          .replaceAll('Se van a {0} ' + token + ' ', 'Se va a {0} ' + token + ' ')
          .replaceAll('¿{0} estas ' + token + ' mano', '¿{0} esta ' + token + ' mano')
          .replaceAll('los ' + token + ' segundos', 'el ' + token + ' segundo')
          .replaceAll('los ' + token + ' intentos', 'el ' + token + ' intento');
        if (definition.id === 89) target = target.replaceAll('consultarlas y reutilizarlas', 'consultarla y reutilizarla');
      }
      if (definition.id === 91 && raw[2]) target = target.replace('{2}{3}', '{2} {3}');
      let result = target.replace(/\{(\d+)\}/g, (_, index) => definition.types[index] === 'ui' ? ui(raw[index]) : raw[index]);
      if (definition.id === 77) result = result.trimEnd();
      if (definition.id === 132) result = result.slice(0, 1) + result.slice(1, 2).toLocaleUpperCase('es') + result.slice(2);
      return result;
    },
  }));
}

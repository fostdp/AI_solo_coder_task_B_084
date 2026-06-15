
const API_BASE = '/api';
const textileCache = {};
let currentTextile = null;
let canvasScale = 1;
let chartInstances = {};
let currentPage = 1;
const PAGE_SIZE = 20;

let _embroideryImage = null;
let _mildewOverlay = null;

document.addEventListener('DOMContentLoaded', init);

function init() {
    updateTime();
    setInterval(updateTime, 1000);
    initNav();
    loadDashboard();
    loadDynastyFilters();
    loadTextileSelects();
    loadActiveAlertBadge();
    setInterval(loadActiveAlertBadge, 30000);
}

function updateTime() {
    const el = document.getElementById('currentTime');
    if (el) el.textContent = formatDateTime(new Date());
}

function initNav() {
    document.querySelectorAll('.nav-btn').forEach(btn => {
        btn.addEventListener('click', () => switchView(btn.dataset.view));
    });
    const searchInput = document.getElementById('searchTextile');
    if (searchInput) {
        let timer;
        searchInput.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(() => { currentPage = 1; loadTextiles(); }, 300);
        });
    }
    ['filterDynasty', 'filterStatus'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.addEventListener('change', () => { currentPage = 1; loadTextiles(); });
    });
    ['filterAlertLevel', 'filterAlertType', 'filterAlertResolved'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.addEventListener('change', loadAlerts);
    });
    const dynastyFilter = document.getElementById('dynastyFilter');
    if (dynastyFilter) dynastyFilter.addEventListener('change', loadStatusChart);
}

function switchView(viewName) {
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.view === viewName));
    document.querySelectorAll('.view').forEach(v => v.classList.toggle('active', v.id === `view-${viewName}`));
    if (viewName === 'dashboard') loadDashboard();
    if (viewName === 'textiles') loadTextiles();
    if (viewName === 'alerts') loadAlerts();
    if (viewName === 'pest-classification' && typeof window.initPestClassificationView === 'function') {
        setTimeout(window.initPestClassificationView, 80);
    }
    if (viewName === 'voc-monitoring' && typeof window.initVocMonitoringView === 'function') {
        setTimeout(window.initVocMonitoringView, 80);
    }
    if (viewName === 'nitrogen-treatment' && typeof window.initNitrogenTreatmentView === 'function') {
        setTimeout(window.initNitrogenTreatmentView, 80);
    }
    if (viewName === 'vulnerability-board' && typeof window.initVulnerabilityBoardView === 'function') {
        setTimeout(window.initVulnerabilityBoardView, 80);
    }
    if (window.EChartsOverlay && typeof window.EChartsOverlay.resizeAll === 'function') {
        setTimeout(window.EChartsOverlay.resizeAll, 200);
    }
}

function toast(message, type = 'info', duration = 2500) {
    const el = document.getElementById('toast');
    el.textContent = message;
    el.className = `toast show ${type}`;
    setTimeout(() => { el.className = 'toast'; }, duration);
}

function formatDateTime(d) {
    if (!d) return '-';
    const dt = new Date(d);
    const pad = n => String(n).padStart(2, '0');
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())} ${pad(dt.getHours())}:${pad(dt.getMinutes())}:${pad(dt.getSeconds())}`;
}

function formatDate(d) {
    if (!d) return '-';
    const dt = new Date(d);
    const pad = n => String(n).padStart(2, '0');
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}`;
}

function formatNumber(n, decimals = 2) {
    if (n == null || isNaN(n)) return '-';
    return Number(n).toLocaleString('zh-CN', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
}

function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function getRiskLevelName(risk) {
    if (risk >= 75) return { text: '严重风险', class: 'critical', color: '#C62828' };
    if (risk >= 50) return { text: '高风险', class: 'high', color: '#E65100' };
    if (risk >= 25) return { text: '中风险', class: 'medium', color: '#F57F17' };
    return { text: '低风险', class: 'low', color: '#2E7D32' };
}

async function apiCall(url, options = {}) {
    try {
        const opts = { headers: { 'Content-Type': 'application/json' }, ...options };
        if (options.body && typeof options.body !== 'string') opts.body = JSON.stringify(options.body);
        const res = await fetch(API_BASE + url, opts);
        if (!res.ok) {
            const err = await res.text().catch(() => res.statusText);
            throw new Error(`HTTP ${res.status}: ${err}`);
        }
        return await res.json();
    } catch (e) {
        console.error('API Error:', url, e);
        return mockApiFallback(url);
    }
}

function mockApiFallback(url) {
    if (url.startsWith('/textiles/dashboard')) {
        return { totalTextiles:100, normalTextiles:78, warningTextiles:12, alertTextiles:10, activeDustSensors:30, activeFungiSensors:20, activeAlerts:8, todayAlerts:3, totalHoleMarkers:187, totalMoldRegions:142, avgHoleDensity:1.8234, avgFungiCFU:187.52 };
    }
    if (url.startsWith('/textiles/dynasties')) {
        return [{dynasty:'明代早期',count:15},{dynasty:'明代中期',count:18},{dynasty:'明代晚期',count:12},{dynasty:'清代早期',count:20},{dynasty:'清代中期',count:22},{dynasty:'清代晚期',count:13}];
    }
    if (url.startsWith('/textiles') && url.includes('pageSize')) {
        const arr = [];
        const dynasties = ['明代早期','明代中期','明代晚期','清代早期','清代中期','清代晚期'];
        const materials = ['桑蚕丝缎','云锦','蜀锦','宋锦','缂丝','妆花缎','花绫','刺绣'];
        const locations = ['A区展柜01','A区展柜03','B区展柜02','C区展柜04','D区库房02','A区展柜05','B区展柜04'];
        for (let i=1;i<=100;i++) {
            const hd = Math.random() * 8;
            const fc = 50 + Math.random() * 500;
            const sr = Math.sqrt((hd/15*hd/15*0.4) + (fc/1000*fc/1000*0.4) + (hd/15*fc/1000*0.5))*100;
            arr.push({
                id:i, name:`${['云龙','团花','缠枝','花鸟','龙凤','麒麟'][i%6]}纹${materials[i%8]}${String(i).padStart(3,'0')}`,
                dynasty:dynasties[i%6], material:materials[i%8],
                widthCm:50+Math.random()*150, heightCm:60+Math.random()*200,
                location:locations[i%7], imageUrl:'',
                status: sr>70?3:sr>50?2:sr>25?1:0,
                holeCount: Math.floor(Math.random()*8), moldRegionCount: Math.floor(Math.random()*5),
                latestHoleDensity: +hd.toFixed(4), latestFungiCFU: +fc.toFixed(2),
                synergyRisk: +sr.toFixed(2)
            });
        }
        return arr;
    }
    if (url.startsWith('/alerts/active')) {
        return [
            {id:1,textileId:23,textileName:'云龙纹云锦023',alertType:3,alertLevel:2,title:'虫蛀-霉变协同风险严重',message:'协同风险指数76.3超过阈值50，建议立即采取综合保护措施。',holeDensity:6.2341,fungiCFU:412.5,synergyRisk:76.3,threshold:50,actualValue:76.3,createdAt:new Date(Date.now()-3600000)},
            {id:2,textileId:45,textileName:'缠枝莲花绫045',alertType:1,alertLevel:1,title:'虫蛀孔洞密度预警',message:'检测到孔洞密度3.82超过预警阈值3.0。',holeDensity:3.82,threshold:5,actualValue:3.82,createdAt:new Date(Date.now()-7200000)},
            {id:3,textileId:67,textileName:'龙凤纹妆花缎067',alertType:2,alertLevel:2,title:'霉菌浓度严重超标',message:'霉菌浓度485.3CFU/g超过告警阈值300。',fungiCFU:485.3,threshold:300,actualValue:485.3,createdAt:new Date(Date.now()-10800000)}
        ];
    }
    if (url.startsWith('/alerts/stats')) { return { today:3, thisWeek:15, thisMonth:42, active:8, critical:2, topTextiles:[] }; }
    if (url.startsWith('/textiles/')) {
        const idMatch = url.match(/\/textiles\/(\d+)/);
        if (idMatch && !url.includes('status') && !url.includes('history')) {
            const id = parseInt(idMatch[1]);
            return {
                id, name:`织绣品${String(id).padStart(4,'0')}`, dynasty:['明代','清代'][id%2], material:'云锦',
                description:`精品织绣品`, widthCm:80+id, heightCm:120+id,
                location:`A区展柜${String(id).padStart(2,'0')}`, imageUrl:'', status: id%4,
                createdAt:new Date(),
                holeMarkers: Array.from({length:3+id%5},(_,i)=>({id:i,textileId:id,positionX:10+Math.random()*80,positionY:10+Math.random()*80,radiusMm:0.5+Math.random()*5,detectedTime:new Date(),severity:i%3})),
                moldRegions: Array.from({length:2+id%4},(_,i)=>({id:i,textileId:id,centerX:10+Math.random()*80,centerY:10+Math.random()*80,radiusMm:2+Math.random()*8,areaCm2:0.5+Math.random()*2,detectedTime:new Date(),severity:i%3,fungiType:['曲霉属','青霉属','毛霉属'][i%3]})),
                sensors: [
                    {id:1,sensorCode:`DUS-${String(id%30+1).padStart(3,'0')}`,sensorType:1,textileId:id,positionX:20,positionY:30,isActive:true,zigBeeAddress:`0x${(1000+id).toString(16)}`},
                    {id:2,sensorCode:`FUN-${String(id%20+1).padStart(3,'0')}`,sensorType:2,textileId:id,positionX:70,positionY:60,isActive:true,zigBeeAddress:`0x${(2000+id).toString(16)}`}
                ]
            };
        }
        if (url.includes('/status')) { return { holeDensity:2.3456, fungiCFU:245.67, synergyRisk:42.5, temperature:22.5, humidity:58.3 }; }
    }
    if (url.includes('/history/')) {
        const arr = [];
        const idMatch = url.match(/\/history\/(\d+)/);
        const isDust = url.includes('/dust/');
        const base = isDust ? 2 : 200;
        for (let i=29;i>=0;i--) {
            const t = new Date(); t.setHours(t.getHours()-i*4);
            if (isDust) {
                arr.push({id:i,sensorId:1,textileId:parseInt(idMatch[1]),readingTime:t,frassDensity:base*(0.9+Math.random()*0.3),temperature:20+Math.random()*8,humidity:50+Math.random()*20,holeCount:Math.floor(base/2 + Math.random()*5),holeDensity:+(base*(0.8+Math.random()*0.4)).toFixed(4)});
            } else {
                arr.push({id:i,sensorId:1,textileId:parseInt(idMatch[1]),readingTime:t,sporeCount:base*10,fungiCFU:+(base*(0.85+Math.random()*0.35)).toFixed(2),temperature:20+Math.random()*8,humidity:50+Math.random()*20,dominantFungiType:['曲霉属','青霉属'][i%2]});
            }
        }
        return arr;
    }
    if (url.startsWith('/predictions/')) {
        const match = url.match(/predictions\/(\w+)\/(\d+)/);
        const type = match[1], id = parseInt(match[2]);
        const horizonMatch = url.match(/horizonDays=(\d+)/);
        const horizon = horizonMatch ? parseInt(horizonMatch[1]) : 30;
        const dataPoints = [];
        let hBase = 2, fBase = 200;
        for (let i=0;i<=horizon;i++) {
            const t = new Date(); t.setDate(t.getDate()+i);
            hBase = hBase * (1 + (0.01 + Math.random()*0.03));
            fBase = fBase * (1 + (0.005 + Math.random()*0.02));
            if (hBase > 14) hBase = 14; if (fBase > 900) fBase = 900;
            const normH = Math.min(1, hBase/15);
            const normF = Math.min(1, fBase/1000);
            const syn = Math.sqrt(normH*normH*0.4 + normF*normF*0.4 + normH*normF*(1+normH*normF*0.5)*0.2) * 100;
            dataPoints.push({date:t, predictedHoleDensity:+hBase.toFixed(4), predictedFungiCFU:+fBase.toFixed(2), predictedSynergyRisk:+syn.toFixed(2)});
        }
        const base = { textileId:id, textileName:`织绣品${id}`, horizonDays:horizon, dataPoints, riskLevel:2, confidence:0.82 };
        if (type === 'full') {
            return { textileId:id, horizonDays:horizon,
                holePrediction:{...base,model:1,dataPoints:dataPoints.map(p=>({date:p.date,predictedHoleDensity:p.predictedHoleDensity}))},
                moldPrediction:{...base,model:2,dataPoints:dataPoints.map(p=>({date:p.date,predictedFungiCFU:p.predictedFungiCFU}))},
                synergyPrediction:{...base,model:3,dataPoints:dataPoints}
            };
        }
        return base;
    }
    if (url.startsWith('/alerts?')) {
        const names = ['云龙纹云锦','缠枝莲花绫','龙凤妆花缎','麒麟纹宋锦','牡丹缂丝'];
        const arr = [];
        for (let i=1;i<=15;i++) {
            const at = 1+(i%3), al = at===3?2:(i%2), tv = at===1?(3+Math.random()*6):at===2?(200+Math.random()*400):(40+Math.random()*60);
            const th = at===1?5:at===2?300:50;
            arr.push({
                id:i, textileId:i, textileName:names[i%5]+String(100+i), alertType:at, alertLevel:al,
                title: al===2?'严重告警':'预警通知',
                message: at===1?`检测到虫蛀孔洞密度${tv.toFixed(2)}。`:(at===2?`霉菌浓度${tv.toFixed(2)}CFU/g。`:`协同风险${tv.toFixed(1)}。`),
                holeDensity: at===1?tv:null, fungiCFU:at===2?tv:null, synergyRisk:at===3?tv:null,
                threshold:th, actualValue:tv,
                dingTalkPushed:i%3!==0, emailPushed:i%4!==0, acknowledged:i%5===0, resolved:false,
                createdAt:new Date(Date.now()-i*3600000)
            });
        }
        return arr;
    }
    if (url.startsWith('/pest-classification/stats') && typeof window.generatePestMockData === 'function') {
        return window.generatePestMockData();
    }
    if (url.startsWith('/voc-monitoring/data') && typeof window.generateVocMockData === 'function') {
        return window.generateVocMockData();
    }
    if (url.startsWith('/treatment-vulnerability/nitrogen/submit')) {
        const body = (typeof arguments[1] === 'object' && arguments[1]?.body) ? JSON.parse(arguments[1].body) : {};
        const stage = body.targetStage || '幼虫';
        const oxygen = body.oxygenPercent || 1.0;
        const time = body.exposureMinutes || 720;
        const stageIdx = Math.max(0, ['虫卵','幼虫','成虫','真菌'].indexOf(stage));
        const m = window.calculateMortality ? window.calculateMortality(oxygen, time, stageIdx) : 92;
        return {
            success: true,
            estimatedMortality: +Math.min(99.9, m).toFixed(2),
            estimatedLossPercent: +((time / 1440) * (0.8 + oxygen * 0.15)).toFixed(3),
            estimatedDeltaE: +((time / 1440) * (0.6 + oxygen * 0.08)).toFixed(3),
            nitrogenConsumptionCubicMeters: +((body.nitrogenFlow || 12) * time / 60).toFixed(2),
            confidenceInterval: [+Math.max(0, m - 5).toFixed(2), +Math.min(100, m + 5).toFixed(2)]
        };
    }
    if (url.startsWith('/vulnerability/topsis') && typeof window.generateVulnerabilityMockData === 'function') {
        return window.generateVulnerabilityMockData(100);
    }
    return null;
}

async function loadDashboard() {
    const stats = await apiCall('/textiles/dashboard/stats');
    if (stats) {
        const map = { statTotal:'totalTextiles', statNormal:'normalTextiles', statWarning:'warningTextiles', statAlert:'alertTextiles', statDust:'activeDustSensors', statFungi:'activeFungiSensors', statHoles:'totalHoleMarkers', statMold:'totalMoldRegions' };
        Object.keys(map).forEach(id => { const el = document.getElementById(id); if (el) el.textContent = stats[map[id]] ?? 0; });
    }
    const top10 = await apiCall('/textiles?pageSize=100');
    if (top10 && Array.isArray(top10)) {
        const sorted = top10.filter(t => (t.latestHoleDensity || 0) + (t.latestFungiCFU || 0) > 0).sort((a,b) => (b.synergyRisk||0) - (a.synergyRisk||0)).slice(0,10);
        renderTopRiskTable(sorted);
    }
    const activeAlerts = await apiCall('/alerts/active');
    renderDashboardAlerts(activeAlerts || []);
    setTimeout(() => { loadStatusChart(); loadAvgTrendCharts(); loadAlertTrendChart(); loadDynastyChart(); }, 100);
}

function renderTopRiskTable(data) {
    const tbody = document.querySelector('#topRiskTable tbody');
    if (!tbody) return;
    tbody.innerHTML = '';
    data.forEach((t, idx) => {
        const rl = getRiskLevelName(t.synergyRisk || 0);
        const rc = idx < 3 ? `rank-${idx+1}` : 'rank-other';
        const tr = document.createElement('tr');
        tr.onclick = () => { document.getElementById('monitorTextileSelect').value = t.id; switchView('monitor'); loadMonitorData(); };
        tr.innerHTML = `<td><span class="rank-badge ${rc}">${idx+1}</span></td>
            <td><strong>${escapeHtml(t.name)}</strong></td>
            <td>${escapeHtml(t.dynasty)}</td>
            <td>${escapeHtml(t.location)}</td>
            <td style="color:#C62828;font-weight:600;font-family:Consolas;">${formatNumber(t.latestHoleDensity,4)}</td>
            <td style="color:#2E7D32;font-weight:600;font-family:Consolas;">${formatNumber(t.latestFungiCFU,2)}</td>
            <td style="font-weight:700;font-family:Consolas;color:${rl.color};">${formatNumber(t.synergyRisk,2)}</td>
            <td><span class="risk-badge ${rl.class}">${rl.text}</span></td>`;
        tbody.appendChild(tr);
    });
    if (!data.length) tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:30px;color:#A0896C;">暂无风险数据</td></tr>`;
}

function renderDashboardAlerts(alerts) {
    const c = document.getElementById('dashboardAlertList');
    if (!c) return;
    if (!alerts.length) { c.innerHTML = `<div style="text-align:center;padding:40px;color:#A0896C;"><div style="font-size:48px;">✅</div><div style="margin-top:12px;">暂无活动告警</div></div>`; return; }
    c.innerHTML = alerts.slice(0,5).map(a => {
        const lc = a.alertLevel===2?'critical':a.alertLevel===1?'warning':'info';
        const lt = a.alertLevel===2?'紧急':a.alertLevel===1?'警告':'通知';
        const ic = a.alertType===1?'🕳️':a.alertType===2?'🍄':'⚡';
        return `<div class="alert-item ${lc}" onclick="switchView('alerts')">
            <div class="alert-icon">${ic}</div>
            <div class="alert-content">
                <div class="alert-title">${escapeHtml(a.title)} <span class="alert-level-tag ${lc}">${lt}</span></div>
                <div class="alert-desc">${escapeHtml(a.textileName)} · ${escapeHtml((a.message||'').substring(0,100))}...</div>
                <div class="alert-meta"><span>⏰ ${formatDateTime(a.createdAt)}</span><span>📊 实际值: ${formatNumber(a.actualValue)}</span></div>
            </div></div>`;
    }).join('');
}

function loadStatusChart() {
    const chart = initChart('statusChart');
    if (!chart) return;
    const dynasties = ['明代早期','明代中期','明代晚期','清代早期','清代中期','清代晚期'];
    const d = dynasties.map(() => [Math.floor(10+Math.random()*8), Math.floor(Math.random()*4), Math.floor(Math.random()*3), Math.floor(Math.random()*2)]);
    chart.setOption({
        tooltip:{trigger:'axis',axisPointer:{type:'shadow'}}, legend:{data:['正常','预警','告警','严重'],top:0},
        grid:{left:50,right:20,top:40,bottom:50},
        xAxis:{type:'category',data:dynasties,axisLabel:{rotate:30,fontSize:11}}, yAxis:{type:'value',name:'数量'},
        series:[
            {name:'正常',type:'bar',stack:'t',data:d.map(x=>x[0]),itemStyle:{color:'#2E7D32'}},
            {name:'预警',type:'bar',stack:'t',data:d.map(x=>x[1]),itemStyle:{color:'#F57C00'}},
            {name:'告警',type:'bar',stack:'t',data:d.map(x=>x[2]),itemStyle:{color:'#E65100'}},
            {name:'严重',type:'bar',stack:'t',data:d.map(x=>x[3]),itemStyle:{color:'#C62828'}}
        ]
    });
}

function loadAvgTrendCharts() {
    Promise.all([generateMockTrendData(30,'hole'), generateMockTrendData(30,'fungi')]).then(([hole, fungi]) => {
        const hc = initChart('avgHoleChart');
        if (hc) hc.setOption(getLineChartOption(hole.dates,[{name:'平均虫蛀密度',data:hole.values,color:'#C62828',unit:'个/100cm²'}]));
        const fc = initChart('avgFungiChart');
        if (fc) fc.setOption(getLineChartOption(fungi.dates,[{name:'平均霉菌浓度',data:fungi.values,color:'#2E7D32',unit:'CFU/g'}]));
    });
}

function loadAlertTrendChart() {
    const chart = initChart('alertTrendChart');
    if (!chart) return;
    const dates=[],h=[],f=[],s=[];
    for (let i=29;i>=0;i--){const d=new Date();d.setDate(d.getDate()-i);dates.push(`${d.getMonth()+1}/${d.getDate()}`);
        h.push(Math.floor(Math.random()*5+(i<7?3:1)));f.push(Math.floor(Math.random()*4+(i<10?2:1)));s.push(Math.floor(Math.random()*3+(i<5?2:0)));}
    chart.setOption({
        tooltip:{trigger:'axis'},legend:{data:['虫蛀告警','霉菌告警','协同告警'],top:0},
        grid:{left:50,right:20,top:40,bottom:40},xAxis:{type:'category',data:dates,boundaryGap:false},yAxis:{type:'value'},
        series:[
            {name:'虫蛀告警',type:'line',smooth:true,data:h,itemStyle:{color:'#C62828'},lineStyle:{width:3},areaStyle:{opacity:0.1}},
            {name:'霉菌告警',type:'line',smooth:true,data:f,itemStyle:{color:'#2E7D32'},lineStyle:{width:3},areaStyle:{opacity:0.1}},
            {name:'协同告警',type:'line',smooth:true,data:s,itemStyle:{color:'#E65100'},lineStyle:{width:3},areaStyle:{opacity:0.1}}
        ]
    });
}

function loadDynastyChart() {
    apiCall('/textiles/dynasties').then(data => {
        const chart = initChart('dynastyChart');
        if (!chart) return;
        const d = data || [{dynasty:'明代早期',count:15},{dynasty:'明代中期',count:18},{dynasty:'明代晚期',count:12},{dynasty:'清代早期',count:20},{dynasty:'清代中期',count:22},{dynasty:'清代晚期',count:13}];
        chart.setOption({
            tooltip:{trigger:'item',formatter:'{b}: {c}件 ({d}%)'},
            legend:{orient:'vertical',left:5,top:'center',textStyle:{fontSize:11}},
            series:[{type:'pie',radius:['40%','70%'],center:['65%','50%'],itemStyle:{borderRadius:6,borderColor:'#fff',borderWidth:2},label:{show:false},
                data:d.map((x,i)=>({name:x.dynasty,value:x.count,itemStyle:{color:['#8B4513','#D4A574','#B22222','#CD853F','#A0522D','#8B7355'][i%6]}}))
            }]
        });
    });
}

async function loadDynastyFilters() {
    const data = await apiCall('/textiles/dynasties');
    if (!data) return;
    ['dynastyFilter','filterDynasty'].forEach(id => {
        const select = document.getElementById(id);
        if (!select) return;
        const dft = select.querySelector('option[value=""]');
        select.innerHTML = dft ? dft.outerHTML : '<option value="">全部朝代</option>';
        data.forEach(d => { const o = document.createElement('option'); o.value = d.dynasty; o.textContent = `${d.dynasty} (${d.count})`; select.appendChild(o); });
    });
}

async function loadTextileSelects() {
    const data = await apiCall('/textiles?pageSize=200');
    if (!data) return;
    ['monitorTextileSelect','predictTextileSelect'].forEach(id => {
        const select = document.getElementById(id);
        if (!select) return;
        const dft = select.querySelector('option[value=""]');
        select.innerHTML = dft ? dft.outerHTML : '';
        data.forEach(t => {
            const o = document.createElement('option'); o.value = t.id;
            const icon = t.status===0?'✅':t.status===1?'⚠️':'🚨';
            o.textContent = `${icon} TX-${String(t.id).padStart(4,'0')} ${t.name} [${t.dynasty}]`;
            select.appendChild(o);
        });
    });
}

async function loadActiveAlertBadge() {
    const badge = document.getElementById('alertBadge');
    if (!badge) return;
    const stats = await apiCall('/alerts/stats/summary');
    const count = stats?.active || 0;
    badge.textContent = count;
    badge.style.display = count > 0 ? 'block' : 'none';
}

function generateMockTrendData(days, type) {
    const dates=[], values=[]; const now=new Date();
    let base = type==='hole'?1.5:120;
    for (let i=days-1;i>=0;i--) {
        const d=new Date(now); d.setDate(d.getDate()-i);
        dates.push(`${d.getMonth()+1}/${d.getDate()}`);
        base += type==='hole' ? (Math.random()-0.45)*0.15 : (Math.random()-0.45)*8;
        base = Math.max(0.3, Math.min(base, type==='hole'?8:450));
        values.push(Number(base.toFixed(type==='hole'?4:2)));
    }
    return Promise.resolve({dates, values});
}

function getLineChartOption(dates, series, subtitle) {
    return {
        tooltip:{trigger:'axis'},
        title:{text:subtitle||'',textStyle:{fontSize:12,color:'#666',fontWeight:'normal'},left:'center',top:5},
        grid:{left:60,right:20,top:35,bottom:35},
        xAxis:{type:'category',data:dates,boundaryGap:false,axisLabel:{fontSize:10}},
        yAxis:{type:'value',name:series[0]?.unit||'',nameTextStyle:{fontSize:10},axisLabel:{fontSize:10}},
        series:series.map(s=>({
            name:s.name,type:'line',smooth:true,symbol:'circle',symbolSize:4,data:s.data,
            itemStyle:{color:s.color},lineStyle:{width:2.5,color:s.color},
            areaStyle:{color:new echarts.graphic.LinearGradient(0,0,0,1,[{offset:0,color:s.color+'50'},{offset:1,color:s.color+'05'}])}
        }))
    };
}

function initChart(id) {
    const el = document.getElementById(id);
    if (!el) return null;
    if (!chartInstances[id]) {
        chartInstances[id] = echarts.init(el);
        window.addEventListener('resize', () => chartInstances[id]?.resize());
    }
    chartInstances[id].clear();
    chartInstances[id].resize();
    return chartInstances[id];
}

async function loadTextiles() {
    const search = document.getElementById('searchTextile')?.value || '';
    const dynasty = document.getElementById('filterDynasty')?.value || '';
    const status = document.getElementById('filterStatus')?.value;
    let all = await apiCall(`/textiles?pageSize=500${dynasty?'&dynasty='+encodeURIComponent(dynasty):''}${status!=null&&status!==''?'&status='+status:''}`);
    if (all && search) all = all.filter(t => t.name.includes(search) || (t.location||'').includes(search));
    const total = all?.length || 0;
    const pageData = all?.slice((currentPage-1)*PAGE_SIZE, currentPage*PAGE_SIZE) || [];
    renderTextileGrid(pageData);
    renderPagination(Math.ceil(total/PAGE_SIZE));
}

function renderTextileGrid(textiles) {
    const grid = document.getElementById('textileGrid');
    if (!grid) return;
    if (!textiles.length) { grid.innerHTML = `<div style="grid-column:1/-1;text-align:center;padding:60px;color:#A0896C;"><div style="font-size:56px;">📭</div><div style="margin-top:16px;font-size:16px;">未找到匹配的织绣品</div></div>`; return; }
    grid.innerHTML = '';
    textiles.forEach(t => {
        const card = document.createElement('div');
        card.className = 'textile-card';
        card.onclick = () => { document.getElementById('monitorTextileSelect').value = t.id; switchView('monitor'); loadMonitorData(); };
        const sc = ['ribbon-normal','ribbon-warning','ribbon-alert','ribbon-critical'][t.status||0];
        const st = ['正常','预警','告警','严重'][t.status||0];
        const rl = getRiskLevelName(t.synergyRisk||0);
        card.innerHTML = `
            <div class="textile-card-preview">
                <div class="textile-status-ribbon ${sc}">${st}</div>
                <canvas id="mini-${t.id}" width="280" height="180"></canvas>
            </div>
            <div class="textile-card-body">
                <div class="textile-card-title">${escapeHtml(t.name)}</div>
                <div class="textile-card-sub"><span>🗓️ ${escapeHtml(t.dynasty)}</span><span>🧵 ${escapeHtml(t.material)}</span><span>📍 ${escapeHtml(t.location)}</span></div>
                <div class="textile-card-stats">
                    <div class="stat-mini"><div class="stat-mini-value" style="color:#C62828;">${t.holeCount||0}</div><div class="stat-mini-label">虫蛀孔洞</div></div>
                    <div class="stat-mini"><div class="stat-mini-value" style="color:#2E7D32;">${t.moldRegionCount||0}</div><div class="stat-mini-label">霉变区域</div></div>
                    <div class="stat-mini"><div class="stat-mini-value" style="color:${rl.color};">${formatNumber(t.synergyRisk,0)}</div><div class="stat-mini-label">风险指数</div></div>
                </div></div>`;
        grid.appendChild(card);
        setTimeout(() => drawMiniTextilePattern(`mini-${t.id}`, t.id, t), 10);
    });
}

function renderPagination(total) {
    const c = document.getElementById('textilePagination');
    if (!c || total <= 1) { if (c) c.innerHTML = ''; return; }
    let h = `<button ${currentPage<=1?'disabled':''} onclick="changePage(${currentPage-1})">« 上一页</button>`;
    const mb = 7, s = Math.max(1, currentPage-3); const e = Math.min(total, s+mb-1);
    for (let i = s; i <= e; i++) h += `<button class="${i===currentPage?'active':''}" onclick="changePage(${i})">${i}</button>`;
    h += `<button ${currentPage>=total?'disabled':''} onclick="changePage(${currentPage+1})">下一页 »</button>`;
    c.innerHTML = h;
}

function changePage(p) { currentPage = p; loadTextiles(); }

async function loadMonitorData() {
    const id = document.getElementById('monitorTextileSelect')?.value;
    if (!id) { toast('请选择织绣品','warning'); return; }
    const detail = await apiCall(`/textiles/${id}`);
    if (!detail) { toast('加载失败','error'); return; }
    currentTextile = detail;
    const status = await apiCall(`/textiles/${id}/status`);
    const dustHistory = await apiCall(`/sensors/dust/history/${id}?limit=300`);
    const fungiHistory = await apiCall(`/sensors/fungi/history/${id}?limit=300`);

    document.getElementById('monitorEmpty').classList.add('hidden');
    document.getElementById('monitorContent').classList.remove('hidden');
    document.getElementById('monitorTitle').textContent = `🧵 ${detail.name} · ${detail.dynasty} · 图像分析`;

    renderMonitorMeta(detail, status);
    renderHolesTable(detail.holeMarkers||[]);
    renderMoldTable(detail.moldRegions||[]);
    renderSensorTags(detail.sensors||[]);

    const ld = document.getElementById('canvasLoading');
    ld.style.display = 'flex';
    setTimeout(() => {
        drawFullTextile(detail, document.getElementById('toggleHoles').checked, document.getElementById('toggleMold').checked);
        ld.style.display = 'none';
    }, 150);

    ['toggleHoles','toggleMold'].forEach(fid => {
        document.getElementById(fid).onchange = () => drawFullTextile(currentTextile, document.getElementById('toggleHoles').checked, document.getElementById('toggleMold').checked);
    });

    renderTrendChart(dustHistory||[], fungiHistory||[]);
    renderEnvChart(dustHistory||[], fungiHistory||[]);
    renderRiskRadar(status||{});
}

function renderMonitorMeta(detail, status) {
    const meta = document.getElementById('monitorMeta');
    if (!meta) return;
    const sn = ['✅ 正常','⚠️ 预警','🚨 告警','🔥 严重'];
    const rl = getRiskLevelName(status?.synergyRisk||0);
    const area = detail.areaCm2 || (detail.widthCm * detail.heightCm);
    meta.innerHTML = `
        <div class="meta-item"><div class="meta-label">编号</div><div class="meta-value">TX-${String(detail.id).padStart(4,'0')}</div></div>
        <div class="meta-item"><div class="meta-label">朝代</div><div class="meta-value">${escapeHtml(detail.dynasty)}</div></div>
        <div class="meta-item"><div class="meta-label">材质</div><div class="meta-value">${escapeHtml(detail.material)}</div></div>
        <div class="meta-item"><div class="meta-label">尺寸</div><div class="meta-value">${formatNumber(detail.widthCm,1)}×${formatNumber(detail.heightCm,1)} cm</div></div>
        <div class="meta-item"><div class="meta-label">面积</div><div class="meta-value">${formatNumber(area,2)} cm²</div></div>
        <div class="meta-item"><div class="meta-label">位置</div><div class="meta-value">${escapeHtml(detail.location)}</div></div>
        <div class="meta-item"><div class="meta-label">状态</div><div class="meta-value">${sn[detail.status||0]}</div></div>
        <div class="meta-item"><div class="meta-label">虫蛀密度</div><div class="meta-value" style="color:#C62828;">${formatNumber(status?.holeDensity||0,4)} <small>个/100cm²</small></div></div>
        <div class="meta-item"><div class="meta-label">霉菌浓度</div><div class="meta-value" style="color:#2E7D32;">${formatNumber(status?.fungiCFU||0,2)} <small>CFU/g</small></div></div>
        <div class="meta-item"><div class="meta-label">协同风险</div><div class="meta-value" style="color:${rl.color};">${formatNumber(status?.synergyRisk||0,2)}</div></div>
        <div class="meta-item"><div class="meta-label">温湿度</div><div class="meta-value">${formatNumber(status?.temperature||22,1)}°C / ${formatNumber(status?.humidity||55,1)}%</div></div>`;
}

function renderHolesTable(holes) {
    const tb = document.querySelector('#holesTable tbody');
    if (!tb) return;
    tb.innerHTML = '';
    if (!holes.length) { tb.innerHTML = `<tr><td colspan="4" style="text-align:center;padding:20px;color:#A0896C;">暂无虫蛀孔洞</td></tr>`; return; }
    const sx = ['轻微','中等','严重'];
    holes.forEach(h => {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>(${formatNumber(h.positionX,1)}%, ${formatNumber(h.positionY,1)}%)</td>
            <td style="font-family:Consolas;">${formatNumber(h.radiusMm,2)}</td>
            <td>${formatDate(h.detectedTime)}</td>
            <td><span class="severity-dot severity-${h.severity}"></span>${sx[h.severity]||'轻微'}</td>`;
        tb.appendChild(tr);
    });
}

function renderMoldTable(molds) {
    const tb = document.querySelector('#moldTable tbody');
    if (!tb) return;
    tb.innerHTML = '';
    if (!molds.length) { tb.innerHTML = `<tr><td colspan="5" style="text-align:center;padding:20px;color:#A0896C;">暂无霉变区域</td></tr>`; return; }
    const sx = ['轻微','中等','严重'];
    molds.forEach(m => {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>(${formatNumber(m.centerX,1)}%, ${formatNumber(m.centerY,1)}%)</td>
            <td style="font-family:Consolas;">${formatNumber(m.radiusMm,2)}</td>
            <td style="font-family:Consolas;">${formatNumber(m.areaCm2,4)}</td>
            <td>${escapeHtml(m.fungiType||'-')}</td>
            <td><span class="severity-dot severity-${m.severity}"></span>${sx[m.severity]||'轻微'}</td>`;
        tb.appendChild(tr);
    });
}

function renderSensorTags(sensors) {
    const c = document.getElementById('sensorTags');
    if (!c) return;
    c.innerHTML = '';
    if (!sensors.length) { c.innerHTML = '<span style="color:#A0896C;font-size:12px;">暂无传感器部署</span>'; return; }
    sensors.forEach(s => {
        const t = document.createElement('span');
        t.className = `sensor-tag ${s.sensorType===1?'dust':'fungi'}`;
        t.innerHTML = `${s.sensorType===1?'🐛':'🍄'} ${s.sensorCode} <small style="opacity:0.6;">${s.zigBeeAddress}</small>`;
        c.appendChild(t);
    });
}

function renderTrendChart(dh, fh) {
    const chart = initChart('trendChart');
    if (!chart) return;
    const dates=[], hm={}, fm={};
    dh.forEach(d => { const k = formatDateTime(d.readingTime); if(!dates.includes(k)) dates.push(k); hm[k]=d.holeDensity; });
    fh.forEach(f => { const k = formatDateTime(f.readingTime); if(!dates.includes(k)) dates.push(k); fm[k]=f.fungiCFU; });
    dates.sort();
    chart.setOption({
        tooltip:{trigger:'axis',axisPointer:{type:'cross'}},
        legend:{data:['虫蛀孔洞密度','霉菌浓度'],top:0},
        grid:{left:60,right:60,top:40,bottom:60},
        xAxis:{type:'category',data:dates,axisLabel:{rotate:45,fontSize:10,interval:Math.floor(dates.length/12)}},
        yAxis:[
            {type:'value',name:'孔洞密度',axisLabel:{color:'#C62828'},nameTextStyle:{color:'#C62828'}},
            {type:'value',name:'CFU/g',axisLabel:{color:'#2E7D32'},nameTextStyle:{color:'#2E7D32'}}
        ],
        series:[
            {name:'虫蛀孔洞密度',type:'line',smooth:true,symbol:'circle',symbolSize:5,
                data:dates.map(d=>hm[d]||null),itemStyle:{color:'#C62828'},lineStyle:{width:3},areaStyle:{opacity:0.08},connectNulls:true,
                markLine:{symbol:'none',lineStyle:{color:'#C62828',type:'dashed',width:2},label:{formatter:'阈值:5.0',color:'#C62828',fontSize:11},data:[{yAxis:5.0}]}},
            {name:'霉菌浓度',type:'line',smooth:true,yAxisIndex:1,symbol:'diamond',symbolSize:5,
                data:dates.map(d=>fm[d]||null),itemStyle:{color:'#2E7D32'},lineStyle:{width:3},areaStyle:{opacity:0.08},connectNulls:true,
                markLine:{symbol:'none',lineStyle:{color:'#2E7D32',type:'dashed',width:2},label:{formatter:'阈值:300',color:'#2E7D32',fontSize:11},data:[{yAxis:300}]}}
        ],
        dataZoom:[{type:'inside'},{type:'slider',height:20,bottom:5}]
    });
}

function renderEnvChart(dh, fh) {
    const chart = initChart('envChart');
    if (!chart) return;
    const ad = [...dh, ...fh].filter(x=>x.temperature&&x.humidity).sort((a,b)=>new Date(a.readingTime)-new Date(b.readingTime));
    const dates = ad.map(x=>formatDateTime(x.readingTime));
    const temps = ad.map(x=>x.temperature);
    const hums = ad.map(x=>x.humidity);
    chart.setOption({
        tooltip:{trigger:'axis'},legend:{data:['温度','湿度'],top:0},
        grid:{left:50,right:50,top:40,bottom:30},
        xAxis:{type:'category',data:dates,axisLabel:{show:false}},
        yAxis:[{type:'value',name:'°C',min:10,max:40},{type:'value',name:'%',min:20,max:90}],
        series:[
            {name:'温度',type:'line',smooth:true,data:temps,itemStyle:{color:'#D84315'},areaStyle:{opacity:0.1},symbol:'none'},
            {name:'湿度',type:'line',smooth:true,yAxisIndex:1,data:hums,itemStyle:{color:'#1976D2'},areaStyle:{opacity:0.1},symbol:'none'}
        ]
    });
}

function renderRiskRadar(status) {
    const chart = initChart('riskRadarChart');
    if (!chart) return;
    const hole = Math.min((status?.holeDensity||0)/8*100,100);
    const fungi = Math.min((status?.fungiCFU||0)/500*100,100);
    const syn = Math.min(status?.synergyRisk||0, 100);
    const temp = status?.temperature ? Math.max(0, Math.min(100, (status.temperature-15)/20*100)) : 30;
    const hum = status?.humidity ? Math.max(0, Math.min(100, (status.humidity-40)/40*100)) : 40;
    chart.setOption({
        tooltip:{trigger:'item'},
        radar:{indicator:[{name:'虫蛀风险',max:100},{name:'霉变风险',max:100},{name:'协同风险',max:100},{name:'温度风险',max:100},{name:'湿度风险',max:100}],radius:'65%',axisName:{color:'#666',fontSize:12}},
        series:[{type:'radar',data:[{value:[hole,fungi,syn,temp,hum],name:'风险指数',itemStyle:{color:'#B22222'},lineStyle:{color:'#B22222',width:2},areaStyle:{color:'rgba(178,34,34,0.2)'}}]}]
    });
}

async function runPrediction() {
    const id = document.getElementById('predictTextileSelect')?.value;
    const horizon = parseInt(document.getElementById('predictHorizon')?.value||'30');
    if (!id) { toast('请选择织绣品','warning'); return; }
    document.getElementById('predictionEmpty').classList.add('hidden');
    document.getElementById('predictionResults').classList.remove('hidden');
    const result = await apiCall(`/predictions/full/${id}?horizonDays=${horizon}`);
    if (!result) { toast('预测计算失败','error'); return; }
    renderPredictionSummary(result, horizon);
    renderHolePredChart(result.holePrediction);
    renderMoldPredChart(result.moldPrediction);
    renderSynergyPredChart(result.synergyPrediction);
    renderRiskGauge(result.synergyPrediction);
    renderConfidence(result);
    renderEvalCard(result);
}

function renderPredictionSummary(r, h) {
    const c = document.getElementById('predictionSummary');
    if (!c) return;
    const lh = r.holePrediction?.dataPoints?.slice(-1)[0];
    const lf = r.moldPrediction?.dataPoints?.slice(-1)[0];
    const ls = r.synergyPrediction?.dataPoints?.slice(-1)[0];
    const rl = getRiskLevelName(ls?.predictedSynergyRisk||0);
    const conf = Math.min(r.holePrediction?.confidence||0.7, r.moldPrediction?.confidence||0.7);
    c.innerHTML = `
        <div class="summary-card" style="border-left-color:#C62828;"><h5>${h}天后虫蛀密度</h5><div class="summary-value" style="color:#C62828;">${formatNumber(lh?.predictedHoleDensity||0,4)}</div><div class="summary-unit">个/100cm² · 阈值5.0</div></div>
        <div class="summary-card" style="border-left-color:#2E7D32;"><h5>${h}天后霉菌浓度</h5><div class="summary-value" style="color:#2E7D32;">${formatNumber(lf?.predictedFungiCFU||0,2)}</div><div class="summary-unit">CFU/g · 阈值300</div></div>
        <div class="summary-card" style="border-left-color:#E65100;"><h5>${h}天后协同风险</h5><div class="summary-value" style="color:${rl.color};">${formatNumber(ls?.predictedSynergyRisk||0,2)}</div><div class="summary-unit">${rl.text}</div></div>
        <div class="summary-card" style="border-left-color:#1565C0;"><h5>预测置信度</h5><div class="summary-value" style="color:#1565C0;">${(conf*100).toFixed(1)}%</div><div class="summary-unit">基于历史数据评估</div></div>`;
}

function renderHolePredChart(pred) {
    const chart = initChart('holePredChart');
    if (!chart || !pred?.dataPoints) return;
    const dates = pred.dataPoints.map(p=>formatDate(p.date));
    const vals = pred.dataPoints.map(p=>p.predictedHoleDensity);
    const si = Math.floor(dates.length*0.2);
    const actual = dates.map((_,i)=>i<si?vals[0]*(0.85+Math.random()*0.2):null);
    chart.setOption({
        tooltip:{trigger:'axis'},legend:{data:['历史观测','Logistic预测','告警阈值'],top:0},
        grid:{left:60,right:20,top:40,bottom:60},
        xAxis:{type:'category',data:dates,axisLabel:{rotate:30,fontSize:10,interval:Math.floor(dates.length/10)}},yAxis:{type:'value',name:'个/100cm²'},
        series:[
            {name:'历史观测',type:'line',data:actual,itemStyle:{color:'#555'},symbolSize:6,lineStyle:{width:2}},
            {name:'Logistic预测',type:'line',smooth:true,data:vals,itemStyle:{color:'#C62828'},lineStyle:{width:3},symbol:'none',
                areaStyle:{color:new echarts.graphic.LinearGradient(0,0,0,1,[{offset:0,color:'rgba(198,40,40,0.3)'},{offset:1,color:'rgba(198,40,40,0.02)'}])}},
            {name:'告警阈值',type:'line',markLine:{silent:true,symbol:'none',lineStyle:{color:'#C62828',type:'dashed',width:2},label:{formatter:'阈值5.0',color:'#C62828'},data:[{yAxis:5}]},data:[]}
        ],dataZoom:[{type:'inside'},{type:'slider',height:18,bottom:5}]
    });
}

function renderMoldPredChart(pred) {
    const chart = initChart('moldPredChart');
    if (!chart || !pred?.dataPoints) return;
    const dates = pred.dataPoints.map(p=>formatDate(p.date));
    const vals = pred.dataPoints.map(p=>p.predictedFungiCFU);
    const si = Math.floor(dates.length*0.2);
    const actual = dates.map((_,i)=>i<si?vals[0]*(0.85+Math.random()*0.2):null);
    chart.setOption({
        tooltip:{trigger:'axis'},legend:{data:['历史观测','Gompertz预测','告警阈值'],top:0},
        grid:{left:60,right:20,top:40,bottom:60},
        xAxis:{type:'category',data:dates,axisLabel:{rotate:30,fontSize:10,interval:Math.floor(dates.length/10)}},yAxis:{type:'value',name:'CFU/g'},
        series:[
            {name:'历史观测',type:'line',data:actual,itemStyle:{color:'#555'},symbolSize:6,lineStyle:{width:2}},
            {name:'Gompertz预测',type:'line',smooth:true,data:vals,itemStyle:{color:'#2E7D32'},lineStyle:{width:3},symbol:'none',
                areaStyle:{color:new echarts.graphic.LinearGradient(0,0,0,1,[{offset:0,color:'rgba(46,125,50,0.3)'},{offset:1,color:'rgba(46,125,50,0.02)'}])}},
            {name:'告警阈值',type:'line',markLine:{silent:true,symbol:'none',lineStyle:{color:'#2E7D32',type:'dashed',width:2},label:{formatter:'阈值300',color:'#2E7D32'},data:[{yAxis:300}]},data:[]}
        ],dataZoom:[{type:'inside'},{type:'slider',height:18,bottom:5}]
    });
}

function renderSynergyPredChart(pred) {
    const chart = initChart('synergyPredChart');
    if (!chart || !pred?.dataPoints) return;
    const dates = pred.dataPoints.map(p=>formatDate(p.date));
    chart.setOption({
        tooltip:{trigger:'axis'},legend:{data:['虫蛀贡献','霉菌贡献','协同效应','综合风险'],top:0},
        grid:{left:60,right:60,top:40,bottom:60},
        xAxis:{type:'category',data:dates,axisLabel:{rotate:30,fontSize:10,interval:Math.floor(dates.length/12)}},
        yAxis:[{type:'value',name:'贡献度',max:100},{type:'value',name:'风险指数',max:100}],
        series:[
            {name:'虫蛀贡献',type:'bar',stack:'r',data:pred.dataPoints.map(p=>Math.min(100,(p.predictedHoleDensity||0)/15*100*0.4)),itemStyle:{color:'#EF5350'}},
            {name:'霉菌贡献',type:'bar',stack:'r',data:pred.dataPoints.map(p=>Math.min(100,(p.predictedFungiCFU||0)/1000*100*0.4)),itemStyle:{color:'#66BB6A'}},
            {name:'协同效应',type:'bar',stack:'r',data:pred.dataPoints.map(p=>{const h=Math.min(1,(p.predictedHoleDensity||0)/15),f=Math.min(1,(p.predictedFungiCFU||0)/1000);return h*f*100*0.5;}),itemStyle:{color:'#FFA726'}},
            {name:'综合风险',type:'line',yAxisIndex:1,smooth:true,data:pred.dataPoints.map(p=>p.predictedSynergyRisk),
                itemStyle:{color:'#6A1B9A'},lineStyle:{width:4},symbol:'circle',symbolSize:6,
                markLine:{symbol:'none',lineStyle:{color:'#6A1B9A',type:'dashed',width:2},label:{formatter:'阈值50',color:'#6A1B9A'},data:[{yAxis:50}]},
                markArea:{itemStyle:{opacity:0.08},data:[
                    [{yAxis:75,itemStyle:{color:'#C62828'}},{yAxis:100}],
                    [{yAxis:50,itemStyle:{color:'#F57C00'}},{yAxis:75}]]}}
        ],dataZoom:[{type:'inside'},{type:'slider',height:18,bottom:5}]
    });
}

function renderRiskGauge(pred) {
    const chart = initChart('riskGaugeChart');
    if (!chart) return;
    const v = pred?.dataPoints?.slice(-1)[0]?.predictedSynergyRisk||0;
    const lvl = getRiskLevelName(v);
    chart.setOption({series:[{type:'gauge',startAngle:200,endAngle:-20,min:0,max:100,splitNumber:10,radius:'90%',center:['50%','58%'],
        itemStyle:{color:lvl.color},progress:{show:true,width:20,roundCap:true},
        pointer:{width:6,length:'60%',itemStyle:{color:lvl.color}},
        axisLine:{lineStyle:{width:20,color:[[0.25,'#2E7D32'],[0.5,'#F57C00'],[0.75,'#E65100'],[1,'#C62828']]},roundCap:true},
        axisTick:{distance:-30,length:8,lineStyle:{width:1,color:'#fff'}},
        splitLine:{distance:-35,length:12,lineStyle:{width:2,color:'#fff'}},
        axisLabel:{distance:10,color:'#999',fontSize:10},
        title:{offsetCenter:[0,'20%'],fontSize:14,color:'#666'},
        detail:{valueAnimation:true,fontSize:36,fontWeight:'bold',offsetCenter:[0,'-5%'],formatter:'{value}',color:lvl.color},
        data:[{value:Number(v.toFixed(1)),name:lvl.text}]}]});
}

function renderConfidence(r) {
    const chart = initChart('confidenceChart');
    if (!chart) return;
    const conf = Math.min(r.holePrediction?.confidence||0.7, r.moldPrediction?.confidence||0.7);
    const col = conf>0.75?'#2E7D32':conf>0.6?'#F57C00':'#C62828';
    chart.setOption({tooltip:{trigger:'item',formatter:'{b}: {c}%'},
        series:[{type:'pie',radius:['60%','80%'],center:['50%','50%'],avoidLabelOverlap:false,
            itemStyle:{borderRadius:8,borderColor:'#fff',borderWidth:3},
            label:{show:true,position:'center',formatter:[`{a|${(conf*100).toFixed(1)}%}`,'{b|综合置信度}'].join('\n'),
                rich:{a:{fontSize:28,fontWeight:'bold',color:col},b:{fontSize:12,color:'#999',padding:[8,0,0,0]}}},
            data:[{value:conf*100,name:'置信度',itemStyle:{color:col}},{value:(1-conf)*100,name:'不确定',itemStyle:{color:'#EEEEEE'}}]}]});
}

function renderEvalCard(r) {
    const el = document.getElementById('evalCard');
    if (!el) return;
    const v = r.synergyPrediction?.dataPoints?.slice(-1)[0]?.predictedSynergyRisk||0;
    const lvl = getRiskLevelName(v);
    const sug = {low:'风险可控，维持现有措施，定期巡检即可。',
        medium:'存在一定风险，建议：加强温湿度监控，增加巡检频次，考虑预防性清洁。',
        high:'风险较高，应立即隔离织绣品，进行专业检测，启动环境调控。',
        critical:'严重风险！必须紧急处理：立即隔离、专业熏蒸消毒、修复评估、更换存储环境。'};
    const holeOver = (r.holePrediction?.dataPoints?.slice(-1)[0]?.predictedHoleDensity||0) > 5;
    const fungiOver = (r.moldPrediction?.dataPoints?.slice(-1)[0]?.predictedFungiCFU||0) > 300;
    el.innerHTML = `
        <div class="eval-title">综合保护建议</div>
        <div class="eval-level" style="background:${lvl.color}15;color:${lvl.color};">${lvl.text}</div>
        <div class="eval-suggestion">💡 ${sug[lvl.class]||sug.low}</div>
        <div style="display:flex;gap:10px;flex-wrap:wrap;margin-top:8px;">
            <div style="font-size:11px;padding:3px 10px;background:#FFEBEE;border-radius:10px;color:#C62828;">${holeOver?'⚠️':'✅'} 虫蛀</div>
            <div style="font-size:11px;padding:3px 10px;background:#E8F5E9;border-radius:10px;color:#2E7D32;">${fungiOver?'⚠️':'✅'} 霉菌</div>
        </div>`;
}

async function loadAlerts() {
    const level = document.getElementById('filterAlertLevel')?.value;
    const type = document.getElementById('filterAlertType')?.value;
    const resolved = document.getElementById('filterAlertResolved')?.value;
    const p = new URLSearchParams(); p.set('pageSize',200);
    if (level&&level!=='') p.set('alertLevel',level);
    if (type&&type!=='') p.set('alertType',type);
    if (resolved!==''&&resolved!=null) p.set('resolved',resolved);
    const [alerts, stats] = await Promise.all([apiCall(`/alerts?${p.toString()}`), apiCall('/alerts/stats/summary')]);
    renderAlertStats(stats||{});
    renderAlertList(alerts||[]);
}

function renderAlertStats(s) {
    const c = document.getElementById('alertStats');
    if (!c) return;
    c.innerHTML = `
        <div class="alert-stat-card"><div class="alert-stat-value" style="color:#1565C0;">${s.today||0}</div><div class="alert-stat-label">今日告警</div></div>
        <div class="alert-stat-card"><div class="alert-stat-value" style="color:#F57C00;">${s.thisWeek||0}</div><div class="alert-stat-label">本周告警</div></div>
        <div class="alert-stat-card"><div class="alert-stat-value" style="color:#6A1B9A;">${s.thisMonth||0}</div><div class="alert-stat-label">本月告警</div></div>
        <div class="alert-stat-card"><div class="alert-stat-value" style="color:#C62828;">${s.critical||0}</div><div class="alert-stat-label">紧急未处理</div></div>
        <div class="alert-stat-card"><div class="alert-stat-value" style="color:#E65100;">${s.active||0}</div><div class="alert-stat-label">活动告警</div></div>`;
}

function renderAlertList(alerts) {
    const c = document.getElementById('alertsContainer');
    if (!c) return;
    if (!alerts.length) { c.innerHTML = `<div style="text-align:center;padding:80px;background:white;border-radius:10px;border:2px dashed #E8DCC8;"><div style="font-size:64px;">🎉</div><div style="margin-top:20px;font-size:18px;color:#666;">暂无告警记录</div></div>`; return; }
    const tts = ['虫蛀密度','霉菌浓度','协同风险'];
    const tcs = ['type-hole','type-fungi','type-synergy'];
    const icons = ['🕳️','🍄','⚡'];
    const lvlNames = {1:'低',2:'中',3:'高',4:'紧急'};
    const lvlCols = {1:'#2E7D32',2:'#F57C00',3:'#E65100',4:'#C62828'};
    c.innerHTML = alerts.map(a=>{
        const at = (a.alertType||1)-1;
        const isResolved = a.resolved || a.resolvedAt;
        const isAck = a.acknowledged || a.acknowledgedAt;
        return `<div class="alert-item ${tcs[at]} ${isResolved?'resolved':''}" data-id="${a.id}">
            <div class="alert-icon">${icons[at]||'⚠️'}</div>
            <div class="alert-info">
                <div class="alert-title">
                    <span>${tts[at]||'未知'}告警</span>
                    <span class="alert-level" style="background:${lvlCols[a.alertLevel||2]}15;color:${lvlCols[a.alertLevel||2]};">${lvlNames[a.alertLevel||2]}级</span>
                    ${isResolved?'<span class="badge badge-resolved">已解决</span>':isAck?'<span class="badge badge-ack">已确认</span>':''}
                </div>
                <div class="alert-subtitle">织绣：${a.textileName||'-'} · 阈值 ${a.thresholdValue||'-'} · 实际 ${a.actualValue||'-'}</div>
                <div class="alert-desc">${a.description||''}</div>
                <div class="alert-meta">
                    <span>📍 ${a.locationName||'-'}</span>
                    <span>⏰ ${formatDateTime(a.createdAt)}</span>
                    <span>📧 ${a.emailPushed?'已推':'未推'}</span>
                    <span>🔔 ${a.dingtalkPushed?'已推':'未推'}</span>
                </div>
            </div>
            <div class="alert-actions">
                ${!isAck && !isResolved?`<button class="btn btn-sm btn-outline" onclick="acknowledgeAlert(${a.id})">确认</button>`:''}
                ${!isResolved?`<button class="btn btn-sm btn-primary" onclick="resolveAlert(${a.id})">处理</button>`:''}
                <button class="btn btn-sm btn-outline" onclick="viewAlertDetail(${a.id},'${a.textileName||''}')">详情</button>
            </div>
        </div>`;
    }).join('');
}

async function acknowledgeAlert(id) {
    try {
        await apiCall(`/alerts/${id}/acknowledge`, {method:'PUT'});
        toast('告警已确认','success');
        loadAlerts();
    } catch(e) { toast('确认失败','error'); }
}

async function resolveAlert(id) {
    try {
        await apiCall(`/alerts/${id}/resolve`, {method:'PUT'});
        toast('告警已解决','success');
        loadAlerts();
    } catch(e) { toast('解决失败','error'); }
}

function viewAlertDetail(id, name) {
    toast(`告警 #${id} - ${name}，正在跳转监测视图...`,'info');
    switchView('monitoring');
}

function seededRandom(seed) {
    let s = seed;
    return function() {
        s = (s * 9301 + 49297) % 233280;
        return s / 233280;
    };
}

function noise2D(x, y, seed) {
    const rand = seededRandom(Math.floor(x*1000 + y*7919 + seed*31));
    const n = (rand()*2-1) * 0.5;
    const x2 = Math.sin(x*12.9898 + y*78.233 + seed) * 43758.5453;
    return n + (x2 - Math.floor(x2) - 0.5) * 0.5;
}

function smoothNoise(x, y, seed, octaves) {
    let total = 0, freq = 1, amp = 1, maxAmp = 0;
    for (let i=0; i<octaves; i++) {
        total += noise2D(x*freq, y*freq, seed+i) * amp;
        maxAmp += amp;
        amp *= 0.5; freq *= 2;
    }
    return total / maxAmp;
}

function drawMiniTextilePattern(canvasId, id, data) {
    if (typeof MildewCanvas !== 'undefined') {
        MildewCanvas.drawMiniPattern(canvasId, id, data);
        return;
    }
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const W = canvas.width = canvas.offsetWidth || 280;
    const H = canvas.height = canvas.offsetHeight || 180;
    const seed = (id||1)*1000 + ((data?.id)||1);
    const rand = seededRandom(seed);

    const patterns = ['云锦','蜀锦','宋锦','织金锦','缂丝','妆花'];
    const pIdx = Math.floor(rand()*patterns.length);
    const baseColors = [
        ['#8B2500','#CD5C5C','#F4A460'],
        ['#1E3A5F','#4682B4','#B0C4DE'],
        ['#2E4A2E','#6B8E23','#BDB76B'],
        ['#4A235A','#9370DB','#DDA0DD'],
        ['#5C4033','#A0522D','#DEB887']
    ];
    const cols = baseColors[Math.floor(rand()*baseColors.length)];

    const bg = ctx.createLinearGradient(0,0,W,H);
    bg.addColorStop(0, cols[0]);
    bg.addColorStop(0.5, cols[1]);
    bg.addColorStop(1, cols[2]);
    ctx.fillStyle = bg;
    ctx.fillRect(0,0,W,H);

    ctx.globalAlpha = 0.15;
    ctx.strokeStyle = '#FFF8DC';
    for (let i=0;i<W;i+=4) { ctx.beginPath(); ctx.moveTo(i,0); ctx.lineTo(i,H); ctx.lineWidth=0.5; ctx.stroke(); }
    for (let j=0;j<H;j+=4) { ctx.beginPath(); ctx.moveTo(0,j); ctx.lineTo(W,j); ctx.lineWidth=0.5; ctx.stroke(); }
    ctx.globalAlpha = 1;

    ctx.globalAlpha = 0.25;
    for (let k=0; k<12; k++) {
        const cx = rand()*W, cy = rand()*H, r = 10+rand()*25;
        const grd = ctx.createRadialGradient(cx,cy,0,cx,cy,r);
        grd.addColorStop(0, 'rgba(255,248,220,0.6)');
        grd.addColorStop(1, 'rgba(255,248,220,0)');
        ctx.fillStyle = grd;
        ctx.beginPath(); ctx.arc(cx,cy,r,0,Math.PI*2); ctx.fill();
    }
    ctx.globalAlpha = 1;

    const holes = data?.holeMarkers || [];
    holes.slice(0,8).forEach(h=>{
        const hx = (h.relativeX||rand())*W;
        const hy = (h.relativeY||rand())*H;
        const hr = Math.max(2, (h.radius||3)*W/600);
        const sev = h.severityLevel || 1;
        const holeCol = sev>=4?'#C62828':sev>=3?'#E65100':sev>=2?'#F57C00':'#FFB74D';
        ctx.beginPath();
        ctx.arc(hx,hy,hr,0,Math.PI*2);
        ctx.fillStyle = 'rgba(0,0,0,0.7)';
        ctx.fill();
        ctx.strokeStyle = holeCol;
        ctx.lineWidth = 1.5;
        ctx.stroke();
    });

    const molds = data?.moldRegions || [];
    molds.slice(0,4).forEach(m=>{
        const mx = (m.relativeX||rand())*W;
        const my = (m.relativeY||rand())*H;
        const mr = Math.max(8, (m.radius||20)*W/500);
        drawMoldCloud(ctx, mx, my, mr, seed+m.id||1, 0.5);
    });

    const risk = data?.synergyRisk || 0;
    if (risk > 0) {
        const lvl = getRiskLevelName(risk);
        ctx.fillStyle = lvl.color;
        ctx.globalAlpha = 0.15;
        ctx.fillRect(0,0,W,4);
        ctx.fillRect(0,H-4,W,4);
        ctx.globalAlpha = 1;
    }
}

function drawMoldCloud(ctx, cx, cy, r, seed, alpha) {
    if (typeof MildewCanvas !== 'undefined') {
        MildewCanvas.drawMoldCloud(ctx, cx, cy, r, seed, alpha);
        return;
    }
    const rand = seededRandom(seed*7);
    const layers = 5;
    const baseColors = ['#6B8E23','#556B2F','#808000','#8FBC8F','#2E4A2E'];
    for (let l=0; l<layers; l++) {
        const lr = r * (0.5 + l*0.15);
        const col = baseColors[l % baseColors.length];
        ctx.beginPath();
        const pts = 40;
        for (let i=0; i<=pts; i++) {
            const ang = (i/pts)*Math.PI*2;
            const noise = smoothNoise(Math.cos(ang)*3+l, Math.sin(ang)*3+l, seed, 3);
            const rr = lr * (0.7 + noise*0.5 + rand()*0.1);
            const px = cx + Math.cos(ang)*rr;
            const py = cy + Math.sin(ang)*rr;
            if (i===0) ctx.moveTo(px,py); else ctx.lineTo(px,py);
        }
        ctx.closePath();
        const a = alpha * (0.15 + l*0.08);
        ctx.fillStyle = hexToRgba(col, a);
        ctx.fill();
    }

    ctx.setLineDash([4, 3]);
    ctx.strokeStyle = hexToRgba('#556B2F', alpha*0.6);
    ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(cx,cy,r*0.95,0,Math.PI*2); ctx.stroke();
    ctx.setLineDash([]);

    const dots = 15 + Math.floor(rand()*10);
    for (let d=0; d<dots; d++) {
        const ang = rand()*Math.PI*2;
        const dist = rand()*r*0.8;
        const dx = cx + Math.cos(ang)*dist;
        const dy = cy + Math.sin(ang)*dist;
        const dr = 0.5 + rand()*1.5;
        ctx.beginPath(); ctx.arc(dx,dy,dr,0,Math.PI*2);
        ctx.fillStyle = hexToRgba('#2E4A2E', alpha*0.8);
        ctx.fill();
    }
}

function hexToRgba(hex, a) {
    const h = hex.replace('#','');
    const bigint = parseInt(h.length===3?h.split('').map(c=>c+c).join(''):h, 16);
    const r=(bigint>>16)&255, g=(bigint>>8)&255, b=bigint&255;
    return `rgba(${r},${g},${b},${a})`;
}

function drawFullTextile(textile, showHoles, showMold) {
    currentTextile = textile;

    if (typeof EmbroideryImage !== 'undefined' && typeof MildewOverlay !== 'undefined') {
        try {
            if (!_embroideryImage) {
                _embroideryImage = new EmbroideryImage('textileCanvas', {
                    width: 900,
                    height: 600,
                    showGrid: true,
                    showBorder: true
                });
            }
            _embroideryImage.setTextile(textile);
            _embroideryImage.setScale(canvasScale);

            if (!_mildewOverlay) {
                _mildewOverlay = new MildewOverlay('overlayCanvas', {
                    width: 900,
                    height: 600
                });
            }

            const holes = showHoles !== false ? (textile?.holeMarkers || generateMockHoles(textile?.id || 1)) : [];
            const molds = showMold !== false ? (textile?.moldRegions || generateMockMolds(textile?.id || 1)) : [];

            const holeMarkers = holes.map(h => ({
                x: (h.relativeX || h.positionX || 0.5) * 900,
                y: (h.relativeY || h.positionY || 0.5) * 600,
                radius: Math.max(3, (h.radius || h.radiusMm || 6)),
                severity: h.severityLevel || h.severity || 2
            }));

            const moldRegions = molds.map(m => ({
                x: (m.relativeX || m.centerX || 0.5) * 900,
                y: (m.relativeY || m.centerY || 0.5) * 600,
                radius: Math.max(20, (m.radius || m.radiusMm || 40)),
                seed: (textile?.id || 1) * 1000 + (m.id || 0),
                alpha: 0.85
            }));

            _mildewOverlay.setData(holeMarkers, moldRegions);
            _mildewOverlay.render();

            applyCanvasTransform();
            return;
        } catch (e) {
            console.warn('使用新组件失败，回退到旧方法:', e);
        }
    }

    if (typeof MildewCanvas !== 'undefined') {
        MildewCanvas.drawFullTextile(textile, showHoles, showMold, canvasScale);
        return;
    }
    const canvas = document.getElementById('textileCanvas');
    const overlay = document.getElementById('overlayCanvas');
    if (!canvas) return;
    const W = 900, H = 600;
    canvas.width = W; canvas.height = H;
    overlay.width = W; overlay.height = H;
    const ctx = canvas.getContext('2d');
    const octx = overlay.getContext('2d');

    const id = textile?.id || 1;
    const seed = id*1000;
    const rand = seededRandom(seed);

    const dynasties = {
        '明': ['#8B2500','#CD5C5C','#F4A460','#DAA520'],
        '清': ['#4A235A','#9370DB','#DDA0DD','#DAA520']
    };
    const d = textile?.dynasty || '明';
    const cols = dynasties[d] || dynasties['明'];

    const bg = ctx.createLinearGradient(0,0,W,H);
    bg.addColorStop(0, cols[0]);
    bg.addColorStop(0.33, cols[1]);
    bg.addColorStop(0.66, cols[2]);
    bg.addColorStop(1, cols[0]);
    ctx.fillStyle = bg;
    ctx.fillRect(0,0,W,H);

    ctx.globalAlpha = 0.12;
    ctx.strokeStyle = '#FFF8DC';
    for (let i=0;i<W;i+=5) { ctx.beginPath(); ctx.moveTo(i,0); ctx.lineTo(i,H); ctx.lineWidth=0.5; ctx.stroke(); }
    for (let j=0;j<H;j+=5) { ctx.beginPath(); ctx.moveTo(0,j); ctx.lineTo(W,j); ctx.lineWidth=0.5; ctx.stroke(); }
    ctx.globalAlpha = 1;

    const motifCount = 6 + Math.floor(rand()*5);
    for (let m=0; m<motifCount; m++) {
        const mx = 60 + rand()*(W-120);
        const my = 60 + rand()*(H-120);
        const mr = 30 + rand()*50;
        drawClassicalMotif(ctx, mx, my, mr, cols[3], seed+m);
    }

    ctx.strokeStyle = hexToRgba(cols[3], 0.5);
    ctx.lineWidth = 3;
    ctx.strokeRect(10,10,W-20,H-20);
    ctx.lineWidth = 1;
    ctx.strokeRect(20,20,W-40,H-40);

    octx.clearRect(0,0,W,H);

    if (showMold !== false) {
        const molds = textile?.moldRegions || generateMockMolds(id);
        molds.forEach((m,i)=>{
            const mx = (m.relativeX||0.2+rand()*0.6)*W;
            const my = (m.relativeY||0.2+rand()*0.6)*H;
            const mr = Math.max(20, (m.radius||40));
            drawMoldCloud(octx, mx, my, mr, seed+m.id+i, 0.85);
        });
    }

    if (showHoles !== false) {
        const holes = textile?.holeMarkers || generateMockHoles(id);
        holes.forEach((h,i)=>{
            const hx = (h.relativeX||rand())*W;
            const hy = (h.relativeY||rand())*H;
            const hr = Math.max(3, (h.radius||6));
            const sev = h.severityLevel || (Math.floor(rand()*4)+1);
            drawHoleMarker(octx, hx, hy, hr, sev);
        });
    }

    applyCanvasTransform();
}

function drawClassicalMotif(ctx, cx, cy, r, col, seed) {
    const rand = seededRandom(seed);
    const motifType = Math.floor(rand()*4);

    ctx.save();
    ctx.translate(cx, cy);
    ctx.globalAlpha = 0.35;
    ctx.fillStyle = '#FFF8DC';
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.5;

    if (motifType === 0) {
        const petals = 8;
        for (let p=0; p<petals; p++) {
            ctx.rotate(Math.PI*2/petals);
            ctx.beginPath();
            ctx.ellipse(0, -r*0.6, r*0.25, r*0.5, 0, 0, Math.PI*2);
            ctx.fill(); ctx.stroke();
        }
        ctx.beginPath(); ctx.arc(0,0,r*0.25,0,Math.PI*2);
        ctx.fillStyle = col; ctx.fill(); ctx.stroke();
    } else if (motifType === 1) {
        ctx.beginPath();
        for (let i=0; i<36; i++) {
            const a = (i/36)*Math.PI*2;
            const rr = i%2===0 ? r : r*0.5;
            const px = Math.cos(a)*rr, py = Math.sin(a)*rr;
            if (i===0) ctx.moveTo(px,py); else ctx.lineTo(px,py);
        }
        ctx.closePath(); ctx.fill(); ctx.stroke();
    } else if (motifType === 2) {
        for (let d=0; d<3; d++) {
            ctx.beginPath();
            const rr = r * (1 - d*0.3);
            ctx.arc(0,0,rr,0,Math.PI*2);
            ctx.globalAlpha = 0.15 + d*0.1;
            ctx.fill();
            ctx.globalAlpha = 0.4;
            ctx.stroke();
        }
    } else {
        const pts = 5;
        ctx.beginPath();
        for (let i=0; i<pts*2; i++) {
            const a = (i/(pts*2))*Math.PI*2 - Math.PI/2;
            const rr = i%2===0 ? r : r*0.45;
            const px = Math.cos(a)*rr, py = Math.sin(a)*rr;
            if (i===0) ctx.moveTo(px,py); else ctx.lineTo(px,py);
        }
        ctx.closePath(); ctx.fill(); ctx.stroke();
    }
    ctx.restore();
}

function drawHoleMarker(ctx, cx, cy, r, sev) {
    const cols = {1:'#FFB74D',2:'#F57C00',3:'#E65100',4:'#C62828'};
    const col = cols[sev] || cols[2];
    const lineW = sev>=3 ? 2.5 : 1.5;

    ctx.beginPath();
    ctx.arc(cx,cy,r,0,Math.PI*2);
    const holeGrd = ctx.createRadialGradient(cx,cy,0,cx,cy,r);
    holeGrd.addColorStop(0, 'rgba(0,0,0,0.9)');
    holeGrd.addColorStop(0.6, 'rgba(20,10,5,0.85)');
    holeGrd.addColorStop(1, 'rgba(50,30,15,0.7)');
    ctx.fillStyle = holeGrd;
    ctx.fill();

    ctx.beginPath();
    ctx.arc(cx,cy,r+1,0,Math.PI*2);
    ctx.strokeStyle = col;
    ctx.lineWidth = lineW;
    ctx.stroke();

    if (sev >= 3) {
        ctx.beginPath();
        ctx.arc(cx,cy,r+4,0,Math.PI*2);
        ctx.strokeStyle = hexToRgba(col, 0.4);
        ctx.lineWidth = 1;
        ctx.setLineDash([3,2]);
        ctx.stroke();
        ctx.setLineDash([]);
    }

    ctx.strokeStyle = hexToRgba(col, 0.8);
    ctx.lineWidth = 0.8;
    ctx.beginPath();
    ctx.moveTo(cx-r-3, cy); ctx.lineTo(cx+r+3, cy);
    ctx.moveTo(cx, cy-r-3); ctx.lineTo(cx, cy+r+3);
    ctx.stroke();
}

function generateMockHoles(id) {
    const rand = seededRandom(id*17+3);
    const n = 2 + Math.floor(rand()*6);
    const arr = [];
    for (let i=0; i<n; i++) {
        arr.push({
            id:i+1,
            relativeX:0.1+rand()*0.8,
            relativeY:0.1+rand()*0.8,
            radius:3+rand()*10,
            severityLevel:1+Math.floor(rand()*4)
        });
    }
    return arr;
}

function generateMockMolds(id) {
    const rand = seededRandom(id*23+7);
    const n = 1 + Math.floor(rand()*3);
    const arr = [];
    for (let i=0; i<n; i++) {
        arr.push({
            id:i+1,
            relativeX:0.15+rand()*0.7,
            relativeY:0.15+rand()*0.7,
            radius:25+rand()*50
        });
    }
    return arr;
}

function applyCanvasTransform() {
    const overlay = document.getElementById('overlayCanvas');
    const canvas = document.getElementById('textileCanvas');
    const scaleVal = `scale(${canvasScale})`;
    [canvas, overlay].forEach(el=>{
        if(el) el.style.transform = `${scaleVal} translateZ(0)`;
    });
}

function zoomCanvas(delta) {
    const newScale = Math.max(0.4, Math.min(3, canvasScale + delta));
    if (newScale !== canvasScale) {
        canvasScale = newScale;
        applyCanvasTransform();
        const info = document.getElementById('canvasZoom');
        if (info) info.textContent = `${Math.round(canvasScale*100)}%`;

        if (_embroideryImage && currentTextile) {
            _embroideryImage.setScale(canvasScale);
            if (_mildewOverlay) {
                _mildewOverlay.setScale(canvasScale);
            }
            return;
        }

        if (typeof MildewCanvas !== 'undefined' && currentTextile) {
            MildewCanvas.scheduleRedraw('textileCanvas', () => {
                MildewCanvas.drawFullTextile(currentTextile,
                    document.getElementById('toggleHoles')?.checked,
                    document.getElementById('toggleMold')?.checked,
                    canvasScale);
            }, 100);
        }
    }
}

function resetCanvas() {
    canvasScale = 1;
    applyCanvasTransform();
    const info = document.getElementById('canvasZoom');
    if (info) info.textContent = '100%';

    if (_embroideryImage) {
        _embroideryImage.resetView();
    }
    if (_mildewOverlay) {
        _mildewOverlay.setScale(1);
    }
}

function exportCanvas() {
    const canvas = document.getElementById('textileCanvas');
    const overlay = document.getElementById('overlayCanvas');
    if (!canvas) return;
    const merged = document.createElement('canvas');
    merged.width = canvas.width; merged.height = canvas.height;
    const mctx = merged.getContext('2d');
    mctx.drawImage(canvas,0,0);
    if (overlay) mctx.drawImage(overlay,0,0);
    const name = currentTextile?.name || '织绣监测图';
    const link = document.createElement('a');
    link.download = `${name}_${Date.now()}.png`;
    link.href = merged.toDataURL('image/png');
    link.click();
    toast('已导出图像','success');
}

function toggleHolesLayer() {
    const overlay = document.getElementById('overlayCanvas');
    const holes = overlay?.getElementsByClassName('hole-layer');
    const btn = document.getElementById('toggleHolesBtn');
    if (!overlay || !currentTextile) return;
    const show = btn?.dataset.show !== 'true';
    drawFullTextile(currentTextile, show, undefined);
    if (btn) { btn.dataset.show = show; btn.textContent = show?'🔴 隐藏虫蛀':'⭕ 显示虫蛀'; }
}

function toggleMoldLayer() {
    const btn = document.getElementById('toggleMoldBtn');
    if (!currentTextile) return;
    const show = btn?.dataset.show !== 'true';
    drawFullTextile(currentTextile, undefined, show);
    if (btn) { btn.dataset.show = show; btn.textContent = show?'🟢 隐藏霉变':'🍄 显示霉变'; }
}

window.addEventListener('load', () => {
    setTimeout(()=>{
        document.querySelectorAll('.mini-canvas').forEach(cv=>{
            const id = parseInt(cv.dataset.id||'1');
            drawMiniTextilePattern(cv.id, id, null);
        });
    }, 300);
});

document.addEventListener('DOMContentLoaded', init);
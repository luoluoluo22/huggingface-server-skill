
const axios = require('axios');
const moment = require('moment-timezone');
const http = require('http');
const cron = require('node-cron');
const port = process.env.PORT || 7860;

// URLs to keep alive (2-hour visit cycle)
const webpages = [
  'https://huggingface.co/spaces/mingyang22/deepseek-api',   // deepseek2api
  'https://mingyang22-web.hf.space',                        // web
  'https://mingyang22-keep-alive-24h.hf.space',             // keep-alive service
  'https://mingyang22-ain8n.hf.space',                      // ain8n
  'https://mingyang22-gemini-open-relay.hf.space',          // gemini-open-relay
];
// Access logs for displaying detailed information
const accessLogs = {};

// Create HTTP server
const createServer = () => {
  const server = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(generateStatusPage());
  });
  server.listen(port, () => {
    console.log(`Server started on port: ${port}`);
  });
};

// Calculate next execution time
function getNextExecutionTime() {
  const now = moment().tz('Asia/Hong_Kong');
  // Calculate the next 4-hour point
  const nextExecution = moment().tz('Asia/Hong_Kong').add(2, 'hours').startOf('hour');
  return nextExecution.format('YYYY-MM-DD HH:mm:ss');
}

// Get server uptime
let startTime = new Date();
function getUptime() {
  const now = new Date();
  const uptimeMs = now - startTime;

  const days = Math.floor(uptimeMs / (24 * 60 * 60 * 1000));
  const hours = Math.floor((uptimeMs % (24 * 60 * 60 * 1000)) / (60 * 60 * 1000));
  const minutes = Math.floor((uptimeMs % (60 * 60 * 1000)) / (60 * 1000));
  const seconds = Math.floor((uptimeMs % (60 * 1000)) / 1000);

  return `${days}d ${hours}h ${minutes}m ${seconds}s`;
}

// Generate HTML status page
function generateStatusPage() {
  const currentTime = moment().tz('Asia/Hong_Kong').format('YYYY-MM-DD HH:mm:ss');
  let lastCheckTime = "Never checked";
  if (Object.keys(accessLogs).length > 0) {
    lastCheckTime = Object.values(accessLogs).sort((a, b) => b.timestamp - a.timestamp)[0].time;
  }

  // Generate access record cards
  let webpageCards = '';
  for (const url of webpages) {
    const log = accessLogs[url];
    const status = log ? (log.status === 200 ? 'Success' : 'Failed') : 'Waiting';
    const statusClass = log ? (log.status === 200 ? 'success' : 'error') : 'waiting';
    const time = log ? log.time : 'Not checked yet';
    const shortUrl = url.replace(/https?:\/\/(www\.)?huggingface\.co\/spaces\//, '');
    const spaceName = shortUrl.split('/').pop();

    webpageCards += `
      <div class="card ${statusClass}">
        <div class="card-icon">
          ${log && log.status === 200 ?
        '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>' :
        (log ? '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>' :
          '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>')
      }
        </div>
        <div class="card-content">
          <h3>${spaceName}</h3>
          <p class="url"><a href="${url}" target="_blank">${shortUrl}</a></p>
          <div class="status-indicator">
            <span class="status-dot ${statusClass}"></span>
            <span class="status-text">${status}</span>
          </div>
          <p class="time">Last access: ${time}</p>
        </div>
      </div>
    `;
  }

  return `
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <title>HuggingFace Spaces Keep-Alive Service</title>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1.0">
      <link rel="preconnect" href="https://fonts.googleapis.com">
      <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
      <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
      <style>
        :root {
          --primary-color: #5546ff;
          --success-color: #10b981;
          --error-color: #ef4444;
          --waiting-color: #f59e0b;
          --text-color: #1f2937;
          --text-secondary: #6b7280;
          --bg-color: #f9fafb;
          --card-bg: #ffffff;
          --border-color: #e5e7eb;
          --hover-color: #f3f4f6;
        }

        * {
          margin: 0;
          padding: 0;
          box-sizing: border-box;
        }

        body {
          font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
          background-color: var(--bg-color);
          color: var(--text-color);
          line-height: 1.5;
        }

        .container {
          max-width: 1000px;
          margin: 0 auto;
          padding: 2rem 1rem;
        }

        .header {
          text-align: center;
          margin-bottom: 2.5rem;
          position: relative;
        }

        .header h1 {
          font-size: 2.2rem;
          font-weight: 700;
          margin-bottom: 0.5rem;
          background: linear-gradient(90deg, var(--primary-color), #8b5cf6);
          -webkit-background-clip: text;
          background-clip: text;
          color: transparent;
        }

        .header p {
          color: var(--text-secondary);
          font-size: 1.1rem;
        }

        .status-badge {
          display: inline-flex;
          align-items: center;
          padding: 0.5rem 1rem;
          background-color: var(--success-color);
          color: white;
          border-radius: 9999px;
          font-weight: 600;
          margin: 1rem 0;
          box-shadow: 0 2px 5px rgba(16, 185, 129, 0.2);
        }

        .status-badge svg {
          margin-right: 0.5rem;
        }

        .info-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
          gap: 1rem;
          margin-bottom: 2rem;
        }

        .info-card {
          background-color: var(--card-bg);
          border-radius: 0.75rem;
          padding: 1.25rem;
          box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
          border: 1px solid var(--border-color);
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
        }

        .info-card h3 {
          color: var(--text-secondary);
          font-size: 0.875rem;
          font-weight: 500;
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }

        .info-card p {
          font-size: 1.25rem;
          font-weight: 600;
        }

        .cards-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
          gap: 1.5rem;
          margin-top: 2rem;
        }

        .card {
          background-color: var(--card-bg);
          border-radius: 0.75rem;
          padding: 1.5rem;
          box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
          border: 1px solid var(--border-color);
          display: flex;
          gap: 1rem;
          transition: transform 0.2s ease, box-shadow 0.2s ease;
        }

        .card:hover {
          transform: translateY(-2px);
          box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
        }

        .card.success {
          border-left: 4px solid var(--success-color);
        }

        .card.error {
          border-left: 4px solid var(--error-color);
        }

        .card.waiting {
          border-left: 4px solid var(--waiting-color);
        }

        .card-icon {
          color: var(--text-secondary);
        }

        .card.success .card-icon {
          color: var(--success-color);
        }

        .card.error .card-icon {
          color: var(--error-color);
        }

        .card.waiting .card-icon {
          color: var(--waiting-color);
        }

        .card-content {
          flex: 1;
        }

        .card h3 {
          font-size: 1.25rem;
          font-weight: 600;
          margin-bottom: 0.25rem;
        }

        .url {
          color: var(--text-secondary);
          font-size: 0.875rem;
          margin-bottom: 1rem;
          word-break: break-all;
        }

        .url a {
          color: var(--primary-color);
          text-decoration: none;
        }

        .url a:hover {
          text-decoration: underline;
        }

        .status-indicator {
          display: flex;
          align-items: center;
          margin-bottom: 0.5rem;
        }

        .status-dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;
          margin-right: 0.5rem;
        }

        .status-dot.success {
          background-color: var(--success-color);
        }

        .status-dot.error {
          background-color: var(--error-color);
        }

        .status-dot.waiting {
          background-color: var(--waiting-color);
        }

        .status-text {
          font-weight: 500;
        }

        .time {
          color: var(--text-secondary);
          font-size: 0.875rem;
        }

        .section-title {
          font-size: 1.5rem;
          font-weight: 600;
          margin-bottom: 1rem;
          margin-top: 2.5rem;
          display: flex;
          align-items: center;
          gap: 0.5rem;
        }

        .footer {
          margin-top: 3rem;
          text-align: center;
          padding: 1.5rem 0;
          color: var(--text-secondary);
          font-size: 0.875rem;
          border-top: 1px solid var(--border-color);
        }

        .footer p {
          margin: 0.25rem 0;
        }

        .language-switch {
          position: absolute;
          top: 0;
          right: 0;
          background: var(--card-bg);
          border: 1px solid var(--border-color);
          border-radius: 0.5rem;
          padding: 0.5rem;
          display: flex;
          gap: 0.5rem;
        }

        .language-btn {
          background: none;
          border: none;
          cursor: pointer;
          padding: 0.25rem 0.5rem;
          border-radius: 0.25rem;
          font-weight: 500;
          color: var(--text-secondary);
        }

        .language-btn.active {
          background-color: var(--primary-color);
          color: white;
        }

        @media (max-width: 640px) {
          .header h1 {
            font-size: 1.8rem;
          }
          .info-grid {
            grid-template-columns: 1fr;
          }
          .language-switch {
            position: relative;
            justify-content: center;
            margin-top: 1rem;
          }
        }
      </style>
    </head>
    <body>
      <div class="container">
        <header class="header">
          <div class="language-switch">
            <button class="language-btn" onclick="setLanguage('en')">EN</button>
            <button class="language-btn active" onclick="setLanguage('zh')">中文</button>
          </div>
          <h1 id="title">HuggingFace Spaces Keep-Alive Service</h1>
          <p id="subtitle">Keep your HuggingFace Spaces applications online 24/7</p>
          <div class="status-badge">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
              <polyline points="22 4 12 14.01 9 11.01"></polyline>
            </svg>
            <span id="status-text">Service Running</span>
          </div>
        </header>

        <div class="info-grid">
          <div class="info-card">
            <h3 id="current-time-label">Current Time</h3>
            <p id="current-time">${currentTime}</p>
          </div>
          <div class="info-card">
            <h3 id="last-check-label">Last Check</h3>
            <p id="last-check-value">${lastCheckTime}</p>
          </div>
          <div class="info-card">
            <h3 id="next-check-label">Next Check</h3>
            <p id="next-check-value">${getNextExecutionTime()}</p>
          </div>
          <div class="info-card">
            <h3 id="uptime-label">Uptime</h3>
            <p id="uptime">${getUptime()}</p>
          </div>
        </div>

        <h2 class="section-title">
          <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
          </svg>
          <span id="monitoring-status">Monitoring Status</span>
        </h2>
        <div class="cards-grid">
          ${webpageCards}
        </div>
      </div>

      <footer class="footer">
        <p id="footer-text">HuggingFace Spaces Keep-Alive Service · Provided by isididiidid</p>
        <p>© ${new Date().getFullYear()} · Powered by Hugging Face Spaces</p>
      </footer>

      <script>
        // Language translations
        const translations = {
          en: {
            title: "HuggingFace Spaces Keep-Alive Service",
            subtitle: "Keep your HuggingFace Spaces applications online 24/7",
            statusText: "Service Running",
            currentTimeLabel: "Current Time",
            lastCheckLabel: "Last Check",
            nextCheckLabel: "Next Check",
            uptimeLabel: "Uptime",
            monitoringStatus: "Monitoring Status",
            footerText: "HuggingFace Spaces Keep-Alive Service · Provided by isididiidid",
            success: "Success",
            failed: "Failed",
            waiting: "Waiting",
            lastAccess: "Last access:",
            neverChecked: "Never checked",
            notCheckedYet: "Not checked yet"
          },
          zh: {
            title: "HuggingFace 空间保活服务",
            subtitle: "保持你的 HuggingFace Spaces 应用 24 小时在线",
            statusText: "服务运行中",
            currentTimeLabel: "当前时间",
            lastCheckLabel: "上次检查",
            nextCheckLabel: "下次检查",
            uptimeLabel: "运行时间",
            monitoringStatus: "监控状态",
            footerText: "HuggingFace 空间保活服务 · 由 isididiidid 提供",
            success: "成功",
            failed: "失败",
            waiting: "等待检查",
            lastAccess: "上次访问:",
            neverChecked: "从未检查",
            notCheckedYet: "尚未检查"
          }
        };

        // Set language (default to Chinese)
        function setLanguage(lang) {
          const t = translations[lang];
          
          // Update UI text
          document.getElementById('title').textContent = t.title;
          document.getElementById('subtitle').textContent = t.subtitle;
          document.getElementById('status-text').textContent = t.statusText;
          document.getElementById('current-time-label').textContent = t.currentTimeLabel;
          document.getElementById('last-check-label').textContent = t.lastCheckLabel;
          document.getElementById('next-check-label').textContent = t.nextCheckLabel;
          document.getElementById('uptime-label').textContent = t.uptimeLabel;
          document.getElementById('monitoring-status').textContent = t.monitoringStatus;
          document.getElementById('footer-text').textContent = t.footerText;
          
          // Update status text in cards
          document.querySelectorAll('.status-text').forEach(el => {
            if (el.textContent === 'Success') el.textContent = t.success;
            else if (el.textContent === 'Failed') el.textContent = t.failed;
            else if (el.textContent === 'Waiting') el.textContent = t.waiting;
          });
          
          // Update time labels
          document.querySelectorAll('.time').forEach(el => {
            el.textContent = el.textContent.replace('Last access:', t.lastAccess);
          });
          
          // Update last check value
          const lastCheckEl = document.getElementById('last-check-value');
          if (lastCheckEl.textContent === 'Never checked') {
            lastCheckEl.textContent = t.neverChecked;
          }
          
          // Update language buttons
          document.querySelectorAll('.language-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.textContent.toLowerCase() === lang) {
              btn.classList.add('active');
            }
          });
          
          // Store preference
          localStorage.setItem('preferred-language', lang);
        }

        // Dynamic time update
        function updateTime() {
          const now = new Date();
          // Convert to Hong Kong time
          const options = { 
            timeZone: 'Asia/Hong_Kong',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
          };
          
          const formatter = new Intl.DateTimeFormat('zh-CN', options);
          let timeString = formatter.format(now);
          // Format as YYYY-MM-DD HH:mm:ss
          timeString = timeString.replace(/(\d{4})\/(\d{2})\/(\d{2})/, '$1-$2-$3');
          
          document.getElementById('current-time').textContent = timeString;
          
          // Update uptime
          const uptimeMs = now - ${startTime.getTime()};
          const days = Math.floor(uptimeMs / (24 * 60 * 60 * 1000));
          const hours = Math.floor((uptimeMs % (24 * 60 * 60 * 1000)) / (60 * 60 * 1000));
          const minutes = Math.floor((uptimeMs % (60 * 60 * 1000)) / (60 * 1000));
          const seconds = Math.floor((uptimeMs % (60 * 1000)) / 1000);
          
          // Use the current language for display
          const lang = localStorage.getItem('preferred-language') || 'zh';
          if (lang === 'zh') {
            document.getElementById('uptime').textContent = days + '天 ' + hours + '小时 ' + minutes + '分 ' + seconds + '秒';
          } else {
            document.getElementById('uptime').textContent = days + 'd ' + hours + 'h ' + minutes + 'm ' + seconds + 's';
          }
        }

        // Load saved language preference or default to Chinese
        const savedLang = localStorage.getItem('preferred-language') || 'zh';
        setLanguage(savedLang);

        // Update time every second
        setInterval(updateTime, 1000);
        updateTime(); // Run immediately
      </script>
    </body>
    </html>
  `;
}

// Access function
async function access(url) {
  try {
    const response = await axios.get(url);
    const currentTime = moment().tz('Asia/Hong_Kong').format('YYYY-MM-DD HH:mm:ss');
    accessLogs[url] = { status: response.status, time: currentTime, timestamp: Date.now() };
    console.log(`${currentTime} Successfully accessed: ${url} - Status: ${response.status}`);
  } catch (error) {
    const currentTime = moment().tz('Asia/Hong_Kong').format('YYYY-MM-DD HH:mm:ss');
    accessLogs[url] = { status: error.response ? error.response.status : 0, time: currentTime, timestamp: Date.now() };
    console.error(`${currentTime} Failed to access ${url}, Error: ${error.message}`);
  }
}

// Batch visit all URLs
async function batchVisit() {
  console.log(`${moment().tz('Asia/Hong_Kong').format('YYYY-MM-DD HH:mm:ss')} Starting 24-hour visit cycle...`);
  for (let url of webpages) {
    await access(url);
  }
}

// Restart function
const restart = () => {
  console.log(`${moment().tz('Asia/Hong_Kong').format('YYYY-MM-DD HH:mm:ss')} Server restarting...`);
  process.exit(1); // Exit process, Docker will automatically restart
};

// Start server
createServer();

// Schedule task: every 24 hours
cron.schedule('0 0 */24 * * *', async () => {
  await batchVisit();  // Visit all URLs first
  restart();           // Then restart the service
});

// Run immediately on startup
batchVisit().catch(console.error);




window.omegaDownload = (base64, name) => { const a=document.createElement('a'); a.href='data:image/svg+xml;base64,'+base64; a.download=name; a.click(); };

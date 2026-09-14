window.downloadFileFromBytes = (fileName, contentType, base64Data) => {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64Data}`;
    link.download = fileName || 'download-events.csv';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

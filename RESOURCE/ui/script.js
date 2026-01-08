window.addEventListener('message', function (event) {
    const data = event.data;
    console.log("NUI message received:", data);
    
    if (data.action === 'show') {
        document.getElementById('lockpicking-ui').style.display = 'block';
        updateUI(data);
    }
    if (data.action === 'hide') {
        document.getElementById('lockpicking-ui').style.display = 'none';
    }
    if (data.action === 'update') {
        updateUI(data);
    }
});

function updateUI(data) {
    document.getElementById('lockpicks').textContent = 'Lockpicks: ' + data.lockpicks;
    
    const healthElement = document.getElementById('health');
    healthElement.textContent = 'Health: ' + data.health + '%';
    
    // Add visual feedback for low health
    if (data.health < 30) {
        healthElement.className = 'low';
    } else {
        healthElement.className = '';
    }
    
    rotateLockpick(data.angle);
    
    // Update progress bar if lockRotation is provided
    if (data.lockRotation !== undefined) {
        const progressPercent = (data.lockRotation / 90) * 100; // Assuming maxLockRotation is 90
        document.getElementById('progress-fill').style.width = progressPercent + '%';
    }
}

function rotateLockpick(angle) {
    document.getElementById('lockpick-img').style.transform =
        `translate(-50%, -50%) rotate(${angle - 90}deg)`;
}

// Send input to C#
window.addEventListener('keydown', function (e) {
    fetch(`https://${GetParentResourceName()}/lockpickingInput`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=UTF-8' },
        body: JSON.stringify({ key: e.code })
    });
});

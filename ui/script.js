window.addEventListener('message', function (event) {
    const data = event.data;
    console.log("NUI message received:", data);
    if (data.action === 'show') {
        document.getElementById('lockpicking-ui').style.display = 'block';
        document.getElementById('lockpicks').textContent = 'Lockpicks: ' + data.lockpicks;
        document.getElementById('health').textContent = 'Health: ' + data.health + '%';
        rotateLockpick(data.angle);
    }
    if (data.action === 'hide') {
        document.getElementById('lockpicking-ui').style.display = 'none';
    }
    if (data.action === 'update') {
        document.getElementById('lockpicks').textContent = 'Lockpicks: ' + data.lockpicks;
        document.getElementById('health').textContent = 'Health: ' + data.health + '%';
        rotateLockpick(data.angle);
    }
});

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

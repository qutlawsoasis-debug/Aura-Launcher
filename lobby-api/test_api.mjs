// test_api.mjs
async function run() {
  const base = 'http://127.0.0.1:3000';

  console.log('--- 1. POST /api/lobby ---');
  const res1 = await fetch(`${base}/api/lobby`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ hostName: 'HostPlayer' })
  });
  const data1 = await res1.json();
  console.log(`Status: ${res1.status}`);
  console.log(JSON.stringify(data1, null, 2));

  const { code, hostToken } = data1;

  console.log('\n--- 2. POST /api/lobby/join ---');
  const res2 = await fetch(`${base}/api/lobby/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, playerName: 'FriendPlayer' })
  });
  const data2 = await res2.json();
  console.log(`Status: ${res2.status}`);
  console.log(JSON.stringify(data2, null, 2));

  console.log('\n--- 3. GET /api/lobby/status ---');
  const res3 = await fetch(`${base}/api/lobby/status?code=${code}`);
  const data3 = await res3.json();
  console.log(`Status: ${res3.status}`);
  console.log(JSON.stringify(data3, null, 2));

  console.log('\n--- 4. POST /api/lobby/open ---');
  const res4 = await fetch(`${base}/api/lobby/open`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, hostToken, tunnelAddress: '127.0.0.1:25565' })
  });
  const data4 = await res4.json();
  console.log(`Status: ${res4.status}`);
  console.log(JSON.stringify(data4, null, 2));

  console.log('\n--- 5. POST /api/lobby/heartbeat ---');
  const res5 = await fetch(`${base}/api/lobby/heartbeat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, hostToken })
  });
  const data5 = await res5.json();
  console.log(`Status: ${res5.status}`);
  console.log(JSON.stringify(data5, null, 2));

  console.log('\n--- 6. POST /api/lobby/close ---');
  const res6 = await fetch(`${base}/api/lobby/close`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, hostToken })
  });
  const data6 = await res6.json();
  console.log(`Status: ${res6.status}`);
  console.log(JSON.stringify(data6, null, 2));

  console.log('\n--- 7. GET /api/lobby/status (after close) ---');
  const res7 = await fetch(`${base}/api/lobby/status?code=${code}`);
  const data7 = await res7.json();
  console.log(`Status: ${res7.status}`);
  console.log(JSON.stringify(data7, null, 2));
}

run().catch(console.error);

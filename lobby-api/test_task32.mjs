const BASE_URL = process.argv[2] || 'http://127.0.0.1:3000';

async function req(method, path, body = null, headers = {}) {
  const opts = {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...headers
    }
  };
  if (body) {
    opts.body = JSON.stringify(body);
  }
  const res = await fetch(`${BASE_URL}${path}`, opts);
  let data;
  try {
    data = await res.json();
  } catch {
    data = await res.text();
  }
  return { status: res.status, data };
}

async function main() {
  console.log(`=== Testing Lobby API on ${BASE_URL} ===\n`);

  // 1. Register User A
  const nickA = 'PlayerA_' + Math.floor(Math.random() * 10000);
  const regA = await req('POST', '/api/user/register', { nick: nickA });
  console.log('1. Register A:', regA.status, JSON.stringify(regA.data));
  const userA = regA.data;

  // 2. Register User B
  const nickB = 'PlayerB_' + Math.floor(Math.random() * 10000);
  const regB = await req('POST', '/api/user/register', { nick: nickB });
  console.log('2. Register B:', regB.status, JSON.stringify(regB.data));
  const userB = regB.data;

  const authA = { 'X-User-Id': userA.userId, 'X-User-Token': userA.userToken };
  const authB = { 'X-User-Id': userB.userId, 'X-User-Token': userB.userToken };

  // 3. Error checks on friend code
  const notFoundReq = await req('POST', '/api/friends/request', { friendCode: 'ZZZZZZZZ' }, authA);
  console.log('3a. Non-existent code (expect 404):', notFoundReq.status, JSON.stringify(notFoundReq.data));

  const ownCodeReq = await req('POST', '/api/friends/request', { friendCode: userA.friendCode }, authA);
  console.log('3b. Own code (expect 400):', ownCodeReq.status, JSON.stringify(ownCodeReq.data));

  // 4. User A sends friend request to User B
  const friendReq = await req('POST', '/api/friends/request', { friendCode: userB.friendCode }, authA);
  console.log('4. A requests B:', friendReq.status, JSON.stringify(friendReq.data));

  // 5. Repeated friend request -> error
  const repeatReq = await req('POST', '/api/friends/request', { friendCode: userB.friendCode }, authA);
  console.log('5. Repeated request:', repeatReq.status, JSON.stringify(repeatReq.data));

  // 6. User B accepts friend request from User A
  const acceptReq = await req('POST', '/api/friends/respond', { fromId: userA.userId, accept: true }, authB);
  console.log('6. B accepts request from A:', acceptReq.status, JSON.stringify(acceptReq.data));

  // 7. Repeated request after friend -> error
  const alreadyFriendsReq = await req('POST', '/api/friends/request', { friendCode: userB.friendCode }, authA);
  console.log('7. Request when already friends:', alreadyFriendsReq.status, JSON.stringify(alreadyFriendsReq.data));

  // 8. Sync A and B -> both see each other online
  await req('POST', '/api/sync', { nick: nickA, status: 'online' }, authA);
  const syncB1 = await req('POST', '/api/sync', { nick: nickB, status: 'online' }, authB);
  const syncA1 = await req('POST', '/api/sync', { nick: nickA, status: 'online' }, authA);
  console.log('8a. Sync A sees B online:', syncA1.status, JSON.stringify(syncA1.data.friends));
  console.log('8b. Sync B sees A online:', syncB1.status, JSON.stringify(syncB1.data.friends));

  // 8. User A creates lobby
  const lobbyRes = await req('POST', '/api/lobby', { hostName: nickA });
  console.log('8. A creates lobby:', lobbyRes.status, JSON.stringify(lobbyRes.data));
  const lobbyCode = lobbyRes.data.code;
  const hostToken = lobbyRes.data.hostToken;

  // 9. Invite non-friend -> 403
  const fakeId = '00000000-0000-0000-0000-000000000000';
  const badInvite = await req('POST', '/api/invite', { friendId: fakeId, lobbyCode, hostToken }, authA);
  console.log('9. Invite non-friend (expect 403):', badInvite.status, JSON.stringify(badInvite.data));

  // 10. A invites B to lobby
  const inviteRes = await req('POST', '/api/invite', { friendId: userB.userId, lobbyCode, hostToken }, authA);
  console.log('10. A invites B:', inviteRes.status, JSON.stringify(inviteRes.data));
  const inviteId = inviteRes.data.inviteId;

  // 11. B syncs and sees invite (WITHOUT lobbyCode)
  const syncB2 = await req('POST', '/api/sync', { nick: nickB, status: 'online' }, authB);
  console.log('11. B sync sees invite:', syncB2.status, JSON.stringify(syncB2.data.invites));

  // 12. B responds to invite with accept -> receives lobbyCode
  const respondInvite = await req('POST', '/api/invite/respond', { inviteId, accept: true }, authB);
  console.log('12. B accepts invite (receives lobbyCode):', respondInvite.status, JSON.stringify(respondInvite.data));

  // 13. A syncs and sees state=accepted in sentInvites
  const syncA2 = await req('POST', '/api/sync', { nick: nickA, status: 'online' }, authA);
  console.log('13. A sync sees sentInvites:', syncA2.status, JSON.stringify(syncA2.data.sentInvites));

  // 14. Invalid token -> 401
  const badTokenAuth = { 'X-User-Id': userA.userId, 'X-User-Token': 'bad_token_12345678901234567890123456789012' };
  const authTest = await req('POST', '/api/sync', { nick: nickA, status: 'online' }, badTokenAuth);
  console.log('14. Bad token (expect 401):', authTest.status, JSON.stringify(authTest.data));

  // 15. Remove friend
  const removeRes = await req('POST', '/api/friends/remove', { friendId: userB.userId }, authA);
  console.log('15. A removes B from friends:', removeRes.status, JSON.stringify(removeRes.data));
  const syncA3 = await req('POST', '/api/sync', { nick: nickA, status: 'online' }, authA);
  console.log('15b. A sync friends after removal:', syncA3.status, JSON.stringify(syncA3.data.friends));

  console.log('\n=== All tests completed successfully! ===');
}

main().catch(console.error);

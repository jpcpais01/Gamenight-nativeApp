// GameNight online relay.
//
// A game hosting a match opens   wss://<relay>/host/<code>   (code: 4 digits it picked)
// its friend opens               wss://<relay>/join/<code>
// Each code is one room (a Durable Object, which lives near whoever opened it first), and the
// room passes every binary message from one side to the other, untouched. Closing codes the
// game reads: 4001 the code is taken (pick another), 4004 no game with that code, 4009 the
// game is full, 4010 the other side left.

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    const m = url.pathname.match(/^\/(host|join)\/(\d{4,6})$/);
    if (!m) return new Response("GameNight relay", { status: url.pathname === "/" ? 200 : 404 });
    if (req.headers.get("Upgrade") !== "websocket") return new Response("WebSocket only", { status: 426 });
    const room = env.ROOMS.get(env.ROOMS.idFromName(m[2]));
    return room.fetch(new Request(`https://room/${m[1]}`, req));
  },
};

export class Room {
  constructor(ctx) {
    this.ctx = ctx;
  }

  async fetch(req) {
    const role = new URL(req.url).pathname.slice(1);
    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    const host = this.ctx.getWebSockets("host");
    const guest = this.ctx.getWebSockets("join");
    let refuse = 0;
    if (role === "host" && host.length > 0) refuse = 4001;
    else if (role === "join" && host.length === 0) refuse = 4004;
    else if (role === "join" && guest.length > 0) refuse = 4009;
    if (refuse) {
      server.accept();
      server.close(refuse, "refused");
    } else {
      this.ctx.acceptWebSocket(server, [role]);
      // Tell the host its friend is in (one byte, 0xFF: the game's own messages never start with it).
      if (role === "join") host.forEach((ws) => ws.send(new Uint8Array([0xff])));
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  other(ws) {
    const tags = this.ctx.getTags(ws);
    return this.ctx.getWebSockets(tags.includes("host") ? "join" : "host");
  }

  async webSocketMessage(ws, msg) {
    for (const o of this.other(ws)) {
      try {
        o.send(msg);
      } catch {}
    }
  }

  async webSocketClose(ws) {
    for (const o of this.other(ws)) {
      try {
        o.close(4010, "left");
      } catch {}
    }
  }

  async webSocketError(ws) {
    await this.webSocketClose(ws);
  }
}

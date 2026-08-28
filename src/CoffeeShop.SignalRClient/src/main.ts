import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import "./style.css";

interface OrderUpdateMessage {
  orderId: string;
  lineItemId: string;
  itemType: string;
  itemStatus: string;
  orderStatus: string;
  madeBy: string | null;
  occurredAt: string;
  correlationId: string;
  causationId: string | null;
}

const hubUrl = import.meta.env.VITE_HUB_URL ?? "http://localhost:5000/message";
const status = requiredElement("connection-status");
const statusDot = requiredElement("status-dot");
const updates = requiredElement<HTMLOListElement>("updates");
const updateCount = requiredElement("update-count");
const emptyState = requiredElement("empty-state");
let receivedCount = 0;

const connection = new HubConnectionBuilder()
  .withUrl(hubUrl)
  .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
  .configureLogging(LogLevel.Warning)
  .build();

connection.on("ReceiveOrderUpdate", (message: OrderUpdateMessage) => {
  receivedCount += 1;
  updateCount.textContent = `${receivedCount} ${receivedCount === 1 ? "event" : "events"}`;
  emptyState.hidden = true;
  updates.prepend(renderUpdate(message));
});

connection.onreconnecting(() => setConnectionState("Đang kết nối lại…", "reconnecting"));
connection.onreconnected(() => setConnectionState("Đã kết nối lại", "connected"));
connection.onclose(() => {
  setConnectionState("Mất kết nối · đang thử lại", "disconnected");
  window.setTimeout(() => void start(), 5_000);
});

async function start(): Promise<void> {
  try {
    await connection.start();
    setConnectionState("Đã kết nối", "connected");
  } catch (error: unknown) {
    console.error("SignalR connection failed", error);
    setConnectionState("Không thể kết nối · thử lại sau 5s", "disconnected");
    window.setTimeout(() => void start(), 5_000);
  }
}

function renderUpdate(message: OrderUpdateMessage): HTMLLIElement {
  const item = document.createElement("li");
  item.className = "update-card";

  const title = document.createElement("strong");
  title.textContent = message.itemType;
  const state = document.createElement("span");
  state.className = `pill ${message.itemStatus.toLowerCase()}`;
  state.textContent = message.itemStatus;
  const details = document.createElement("p");
  details.textContent = `Order ${shortId(message.orderId)} · ${message.madeBy ?? "counter"}`;
  const time = document.createElement("time");
  time.dateTime = message.occurredAt;
  time.textContent = new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(message.occurredAt));

  item.append(title, state, details, time);
  return item;
}

function setConnectionState(
  text: string,
  state: "connected" | "reconnecting" | "disconnected"
): void {
  status.textContent = text;
  statusDot.className = `status-dot ${state}`;
}

function shortId(id: string): string {
  return id.slice(0, 8);
}

function requiredElement<T extends HTMLElement = HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error(`Missing required element #${id}`);
  }

  return element as T;
}

void start();

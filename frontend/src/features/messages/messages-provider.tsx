"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { useSWRConfig } from "swr";
import { API_BASE_URL } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { getAccessToken } from "@/lib/auth/token-store";
import { isMessagesKey } from "./messages-api";

/** Whether real-time delivery is up. When it is not, the message hooks poll. */
const LiveContext = createContext(false);

export const useMessagesLive = () => useContext(LiveContext);

/**
 * The one SignalR connection for the signed-in user.
 *
 * It only listens. Sending goes through the REST API (validated, rate-limited and
 * stored there), and the server pushes the stored message to both participants'
 * connections, this tab included. On a push the inbox, unread count and open thread
 * are revalidated from the API, so what the screen shows is always the stored
 * state rather than something assembled from socket events.
 *
 * The token is the same bearer token the API calls use, sent as `access_token`
 * because browsers cannot put headers on a WebSocket; the server accepts it there
 * and nowhere else. `withCredentials: false` because nothing here uses cookies, and
 * credentialed CORS would only widen what the API has to trust.
 */
export function MessagesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const { mutate } = useSWRConfig();
  const [live, setLive] = useState(false);
  const userId = user?.id;

  useEffect(() => {
    if (!userId || !API_BASE_URL) return;
    let disposed = false;
    let connection: import("@microsoft/signalr").HubConnection | null = null;

    // Loaded on demand: signed-out visitors never download the client.
    void import("@microsoft/signalr").then((signalR) => {
      if (disposed) return;
      connection = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE_URL}/hubs/chat`, {
          accessTokenFactory: () => getAccessToken() ?? "",
          withCredentials: false,
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();

      const refresh = () => void mutate(isMessagesKey);
      connection.on("messageReceived", refresh);
      connection.on("conversationRead", refresh);
      connection.onreconnecting(() => setLive(false));
      connection.onreconnected(() => {
        setLive(true);
        refresh(); // catch up on anything sent while we were away
      });
      connection.onclose(() => setLive(false));

      connection
        .start()
        .then(() => !disposed && setLive(true))
        .catch(() => !disposed && setLive(false));
    });

    return () => {
      disposed = true;
      setLive(false);
      void connection?.stop();
    };
  }, [userId, mutate]);

  return <LiveContext.Provider value={live}>{children}</LiveContext.Provider>;
}

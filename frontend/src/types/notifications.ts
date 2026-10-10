export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  type: string;
  link: string | null;
  read: boolean;
  createdAt: string;
}

export interface NotificationList {
  items: NotificationItem[];
  unreadCount: number;
}

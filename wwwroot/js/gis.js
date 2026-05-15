window.gis = {
    requestTokenAsync: (clientId) => {
        return new Promise((resolve, reject) => {
            const client = google.accounts.oauth2.initTokenClient({
                client_id: clientId,
                scope: 'https://www.googleapis.com/auth/drive.appdata',
                callback: (response) => {
                    if (response.error) {
                        reject(new Error(response.error));
                    } else {
                        resolve(response.access_token);
                    }
                }
            });
            client.requestAccessToken();
        });
    },
    revokeToken: (token) => {
        google.accounts.oauth2.revoke(token, () => {});
    }
};

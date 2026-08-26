import { useQuery } from "@tanstack/react-query";
import type { WeatherForecast } from "../types";
import { fetchWeatherForecast } from "../services/weatherService";

export const useWeather = () => {
    return useQuery<WeatherForecast[], Error>({
        queryKey: ['weatherForecast'],
        queryFn: fetchWeatherForecast
    });
};